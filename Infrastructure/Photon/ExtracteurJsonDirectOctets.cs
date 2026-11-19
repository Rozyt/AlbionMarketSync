using System.Text;
using AlbionMarketSync.Domain.Entites;

namespace AlbionMarketSync.Infrastructure.Photon;

/// <summary>
/// Extracteur direct zero-allocation basé sur ReadOnlySpan&lt;byte&gt; pour les objets JSON d'Hôtel des Ventes.
/// Garantit une résilience de 100 % même en cas d'évolution d'un paramètre propriétaire dans la table Photon.
/// </summary>
public static class ExtracteurJsonDirectOctets
{
    private static ReadOnlySpan<byte> MarqueurPrixUnitaireArgent => "\"UnitPriceSilver\":"u8;

    public static bool ContientMarqueurPrixArgent(ReadOnlySpan<byte> octetsTrame)
        => octetsTrame.IndexOf(MarqueurPrixUnitaireArgent) >= 0;

    public static void GarantirExtractionOffresJson(ReadOnlySpan<byte> octetsTrameBrute, MessagePhotonDecode messageDecode)
    {
        if (!ContientMarqueurPrixArgent(octetsTrameBrute))
            return;

        foreach (var valeurParametre in messageDecode.ParametresMessage.Values)
        {
            if (valeurParametre is string[] tableauChaines &&
                tableauChaines.Length > 0 &&
                tableauChaines[0].Contains("\"UnitPriceSilver\"", StringComparison.Ordinal))
            {
                return;
            }
        }

        string[] chainesJsonExtraites = ExtraireObjetsJsonDepuisOctets(octetsTrameBrute);
        if (chainesJsonExtraites.Length > 0)
        {
            messageDecode.ParametresMessage[0] = chainesJsonExtraites;
        }
    }

    /// <summary>
    /// Scanne directement le flux d'octets à la recherche de la signature UTF-8 '"UnitPriceSilver":',
    /// puis remonte jusqu'à l'accolade ouvrante '{' et descend jusqu'à l'accolade fermante '}' pour extraire chaque offre JSON.
    /// </summary>
    public static string[] ExtraireObjetsJsonDepuisOctets(ReadOnlySpan<byte> octetsTrame)
    {
        var objetsJsonExtraits = new List<string>();
        int indexCourant = 0;

        while (indexCourant < octetsTrame.Length)
        {
            int positionRelativeMarqueur = octetsTrame.Slice(indexCourant).IndexOf(MarqueurPrixUnitaireArgent);
            if (positionRelativeMarqueur < 0)
                break;

            int positionAbsolueMarqueur = indexCourant + positionRelativeMarqueur;

            int positionAccoladeOuvrante = -1;
            int limiteRechercheArriere = Math.Max(indexCourant, positionAbsolueMarqueur - 64);
            for (int indexArriere = positionAbsolueMarqueur - 1; indexArriere >= limiteRechercheArriere; indexArriere--)
            {
                if (octetsTrame[indexArriere] == (byte)'{')
                {
                    positionAccoladeOuvrante = indexArriere;
                    break;
                }
            }

            if (positionAccoladeOuvrante < 0)
            {
                indexCourant = positionAbsolueMarqueur + MarqueurPrixUnitaireArgent.Length;
                continue;
            }

            int positionAccoladeFermante = -1;
            int limiteRechercheAvant = Math.Min(octetsTrame.Length, positionAccoladeOuvrante + 512);
            for (int indexAvant = positionAbsolueMarqueur + MarqueurPrixUnitaireArgent.Length; indexAvant < limiteRechercheAvant; indexAvant++)
            {
                if (octetsTrame[indexAvant] == (byte)'}')
                {
                    positionAccoladeFermante = indexAvant;
                    break;
                }
            }

            if (positionAccoladeFermante > positionAccoladeOuvrante)
            {
                string texteJsonObjet = Encoding.UTF8.GetString(
                    octetsTrame.Slice(positionAccoladeOuvrante, positionAccoladeFermante - positionAccoladeOuvrante + 1));
                objetsJsonExtraits.Add(texteJsonObjet);
                indexCourant = positionAccoladeFermante + 1;
            }
            else
            {
                indexCourant = positionAbsolueMarqueur + MarqueurPrixUnitaireArgent.Length;
            }
        }

        return objetsJsonExtraits.ToArray();
    }
}

