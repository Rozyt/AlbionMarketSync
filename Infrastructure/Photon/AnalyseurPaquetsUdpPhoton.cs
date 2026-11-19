using System.Buffers.Binary;
using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Interfaces;

namespace AlbionMarketSync.Infrastructure.Photon;

/// <summary>
/// Analyseur de trames UDP ExitGames Photon implémentant IAnalyseurPaquetsPhoton (Module 2.1).
/// Gère l'en-tête UDP Photon (12 octets + CRC optionnel) et recolle les paquets fragmentés (Types 8 &amp; 15).
/// </summary>
public sealed class AnalyseurPaquetsUdpPhoton : IAnalyseurPaquetsPhoton
{
    private const int TailleEntetePhotonOctets = 12;
    private const int TailleEnteteCommandeOctets = 12;

    private const byte CommandeAccuseReception = 1;
    private const byte CommandeConnexion = 2;
    private const byte CommandeVerificationConnexion = 3;
    private const byte CommandeDeconnexion = 4;
    private const byte CommandePing = 5;
    private const byte CommandeEnvoiFiable = 6;
    private const byte CommandeEnvoiNonFiable = 7;
    private const byte CommandeEnvoiFragmentFiable = 8;
    private const byte CommandeEnvoiFiableNonSequence = 11;
    private const byte CommandeEnvoiFiableNonSequenceAlternatif = 14;
    private const byte CommandeEnvoiFragmentNonSequence = 15;
    private const byte CommandeEnvoiFiableAlternatif2 = 16;

    private readonly Dictionary<(ushort IdentifiantPair, byte IdentifiantCanal, int NumeroSequenceDepart), TamponReassemblageFragments> _tamponsFragments = new();
    private readonly Dictionary<byte, int> _compteurParTypeCommande = new();
    private DateTime _dateDernierNettoyageUtc = DateTime.UtcNow;

    public long NombreTotalPaquetsPhoton { get; private set; }
    public long NombreTotalCommandesTraitees { get; private set; }
    public long NombreTotalFragmentsRecus { get; private set; }
    public long NombreTotalMessagesFragmentesReassembles { get; private set; }
    public long NombreTotalMessagesDecodes { get; private set; }
    public IReadOnlyDictionary<byte, int> CompteurParTypeCommande => _compteurParTypeCommande;

    public event Action<MessagePhotonDecode>? SurMessagePhotonDecode;

    /// <summary>
    /// Analyse un paquet UDP brut : vérifie l'en-tête Photon (12 octets + CRC optionnel 0xCC),
    /// puis parcourt la liste des commandes multiplexées dans ce paquet.
    /// </summary>
    public void TraiterChargeUtileUdp(ReadOnlySpan<byte> donneesUdpBrutes)
    {
        if (donneesUdpBrutes.Length < TailleEntetePhotonOctets)
            return;

        ushort identifiantPair = BinaryPrimitives.ReadUInt16BigEndian(donneesUdpBrutes.Slice(0, 2));
        byte drapeauxEntete = donneesUdpBrutes[2];
        byte nombreCommandes = donneesUdpBrutes[3];

        if (drapeauxEntete == 1 || nombreCommandes == 0 || nombreCommandes > 128)
            return;

        int positionLecture = TailleEntetePhotonOctets;

        if (drapeauxEntete == 0xCC)
        {
            if (donneesUdpBrutes.Length < TailleEntetePhotonOctets + 4)
                return;
            positionLecture += 4;
        }

        NombreTotalPaquetsPhoton++;

        for (int indexCommande = 0; indexCommande < nombreCommandes; indexCommande++)
        {
            if (positionLecture + TailleEnteteCommandeOctets > donneesUdpBrutes.Length)
                break;

            byte typeCommande = donneesUdpBrutes[positionLecture];
            byte identifiantCanal = donneesUdpBrutes[positionLecture + 1];
            int longueurCommande = BinaryPrimitives.ReadInt32BigEndian(donneesUdpBrutes.Slice(positionLecture + 4, 4));

            if (longueurCommande < TailleEnteteCommandeOctets || positionLecture + longueurCommande > donneesUdpBrutes.Length)
                break;

            NombreTotalCommandesTraitees++;
            _compteurParTypeCommande[typeCommande] = _compteurParTypeCommande.GetValueOrDefault(typeCommande) + 1;
            ReadOnlySpan<byte> octetsCommande = donneesUdpBrutes.Slice(positionLecture, longueurCommande);

            switch (typeCommande)
            {
                case CommandeEnvoiFiable:
                case CommandeEnvoiNonFiable:
                case CommandeEnvoiFiableNonSequence:
                case CommandeEnvoiFiableNonSequenceAlternatif:
                case CommandeEnvoiFiableAlternatif2:
                {
                    ReadOnlySpan<byte> chargeUtileApresEntete = octetsCommande.Slice(TailleEnteteCommandeOctets);
                    EssayerAiguillerChargeUtileCommande(chargeUtileApresEntete);
                    break;
                }

                case CommandeEnvoiFragmentFiable:
                case CommandeEnvoiFragmentNonSequence:
                {
                    TraiterCommandeFragmentee(identifiantPair, identifiantCanal, octetsCommande);
                    break;
                }

                case CommandeAccuseReception:
                case CommandeConnexion:
                case CommandeVerificationConnexion:
                case CommandeDeconnexion:
                case CommandePing:
                default:
                    break;
            }

            positionLecture += longueurCommande;
        }

        NettoyerFragmentsExpiresSiNecessaire();
    }

    /// <summary>
    /// Réassemble les commandes Photon fragmentées (commandes 8 et 15, ex: pages d'Hôtel des Ventes de 50 offres dépassant le MTU UDP).
    /// Dès que tous les fragments d'un même numéro de séquence sont reçus, le message complet est transmis au décodeur.
    /// </summary>
    private void TraiterCommandeFragmentee(ushort identifiantPair, byte identifiantCanal, ReadOnlySpan<byte> octetsCommande)
    {
        const int TailleEnteteFragmentOctets = 32;
        if (octetsCommande.Length < TailleEnteteFragmentOctets)
            return;

        int numeroSequenceDepart = BinaryPrimitives.ReadInt32BigEndian(octetsCommande.Slice(12, 4));
        int nombreTotalFragments = BinaryPrimitives.ReadInt32BigEndian(octetsCommande.Slice(16, 4));
        int numeroFragmentActuel = BinaryPrimitives.ReadInt32BigEndian(octetsCommande.Slice(20, 4));
        int longueurTotaleMessage = BinaryPrimitives.ReadInt32BigEndian(octetsCommande.Slice(24, 4));
        int decalageFragmentOctets = BinaryPrimitives.ReadInt32BigEndian(octetsCommande.Slice(28, 4));

        if (nombreTotalFragments <= 0 || nombreTotalFragments > 1024 ||
            numeroFragmentActuel < 0 || numeroFragmentActuel >= nombreTotalFragments ||
            longueurTotaleMessage <= 0 || longueurTotaleMessage > 4 * 1024 * 1024 ||
            decalageFragmentOctets < 0)
        {
            return;
        }

        ReadOnlySpan<byte> donneesFragment = octetsCommande.Slice(TailleEnteteFragmentOctets);
        if (decalageFragmentOctets + donneesFragment.Length > longueurTotaleMessage)
            return;

        NombreTotalFragmentsRecus++;

        var cleMessageFragmente = (identifiantPair, identifiantCanal, numeroSequenceDepart);
        if (!_tamponsFragments.TryGetValue(cleMessageFragmente, out TamponReassemblageFragments? tampon))
        {
            tampon = new TamponReassemblageFragments(longueurTotaleMessage, nombreTotalFragments);
            _tamponsFragments[cleMessageFragmente] = tampon;
        }

        if (tampon.AjouterFragment(numeroFragmentActuel, decalageFragmentOctets, donneesFragment))
        {
            _tamponsFragments.Remove(cleMessageFragmente);
            NombreTotalMessagesFragmentesReassembles++;
            EssayerAiguillerChargeUtileCommande(tampon.OctetsMessageComplet);
        }
    }

    private void EssayerAiguillerChargeUtileCommande(ReadOnlySpan<byte> octetsApresEnteteCommande)
    {
        if (octetsApresEnteteCommande.Length < 3)
            return;

        if (octetsApresEnteteCommande[0] is 0xF3 or 0xFD)
        {
            EssayerDecoderMessage(octetsApresEnteteCommande);
            return;
        }

        if (octetsApresEnteteCommande.Length > 4 && octetsApresEnteteCommande[4] is 0xF3 or 0xFD)
        {
            EssayerDecoderMessage(octetsApresEnteteCommande.Slice(4));
            return;
        }

        EssayerDecoderMessage(octetsApresEnteteCommande);
    }

    private void EssayerDecoderMessage(ReadOnlySpan<byte> octetsMessagePhoton)
    {
        if (AiguilleurDecodageBinairePhoton.EssayerDecoderMessage(octetsMessagePhoton, out MessagePhotonDecode? messageDecode) &&
            messageDecode is not null)
        {
            NombreTotalMessagesDecodes++;
            SurMessagePhotonDecode?.Invoke(messageDecode);
        }
    }

    private void NettoyerFragmentsExpiresSiNecessaire()
    {
        DateTime instantActuelUtc = DateTime.UtcNow;
        if ((instantActuelUtc - _dateDernierNettoyageUtc).TotalSeconds < 10)
            return;

        _dateDernierNettoyageUtc = instantActuelUtc;
        if (_tamponsFragments.Count == 0)
            return;

        var clesExpirees = _tamponsFragments
            .Where(entree => (instantActuelUtc - entree.Value.DateCreationUtc).TotalSeconds > 15)
            .Select(entree => entree.Key)
            .ToList();

        foreach (var cle in clesExpirees)
            _tamponsFragments.Remove(cle);
    }

    private sealed class TamponReassemblageFragments(int longueurTotaleOctets, int nombreTotalFragments)
    {
        public byte[] OctetsMessageComplet { get; } = new byte[longueurTotaleOctets];
        public int NombreTotalFragments { get; } = nombreTotalFragments;
        public DateTime DateCreationUtc { get; } = DateTime.UtcNow;
        private readonly HashSet<int> _numerosFragmentsRecus = new();

        public bool AjouterFragment(int numeroFragment, int decalageOctets, ReadOnlySpan<byte> donneesFragment)
        {
            if (decalageOctets + donneesFragment.Length > OctetsMessageComplet.Length)
                return false;

            donneesFragment.CopyTo(OctetsMessageComplet.AsSpan(decalageOctets, donneesFragment.Length));
            _numerosFragmentsRecus.Add(numeroFragment);
            return _numerosFragmentsRecus.Count >= NombreTotalFragments;
        }
    }
}

