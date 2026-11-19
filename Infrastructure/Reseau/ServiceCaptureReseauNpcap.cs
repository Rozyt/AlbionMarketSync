using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using AlbionMarketSync.Application.Configuration;
using AlbionMarketSync.Domain.Enumerations;
using AlbionMarketSync.Domain.Interfaces;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace AlbionMarketSync.Infrastructure.Reseau;

/// <summary>
/// Service de capture réseau passive Npcap / SharpPcap (Module 1) implémentant IServiceCapturePaquetsReseau.
/// Compatible Ethernet, Wi-Fi et interfaces VPN/TUN (ExitLag).
/// </summary>
public sealed partial class ServiceCaptureReseauNpcap : IServiceCapturePaquetsReseau
{
    private const string RepertoirePiloteNpcap = @"C:\Windows\System32\Npcap";
    private const string CheminBibliothequeWpcap = @"C:\Windows\System32\Npcap\wpcap.dll";

    [LibraryImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DefinirRepertoireBibliothequesDll(string cheminRepertoire);

    private readonly ConfigurationCaptureReseau _configurationCapture;
    private readonly FrozenSet<int> _portsUdpAlbion;
    private readonly List<LibPcapLiveDevice> _cartesReseauActives = new();
    private readonly Channel<byte[]> _canalPaquetsUdp;
    private CancellationTokenSource? _sourceAnnulation;
    private Task? _tacheConsommationCanal;

    public long NombreTotalPaquetsUdpCaptures { get; private set; }
    public DateTime? DateDernierPaquetRecuUtc { get; private set; }
    public string? NomCarteReseauActive { get; private set; }

    public EtatSanteCaptureReseau EtatSanteCapture
    {
        get
        {
            if (_cartesReseauActives.Count == 0)
                return EtatSanteCaptureReseau.ErreurPiloteNpcap;

            if (DateDernierPaquetRecuUtc.HasValue && (DateTime.UtcNow - DateDernierPaquetRecuUtc.Value).TotalSeconds < 30)
                return EtatSanteCaptureReseau.CaptureTraficAlbionActive;

            return EtatSanteCaptureReseau.EnEcouteAttenteJeu;
        }
    }

    public event Action<byte[]>? SurPaquetUdpCapture;
    public event Action<string>? SurMessageStatutReseau;

    public ServiceCaptureReseauNpcap(ConfigurationCaptureReseau configurationCapture)
    {
        _configurationCapture = configurationCapture;
        _portsUdpAlbion = configurationCapture.PortsUdpAlbion.ToFrozenSet();
        _canalPaquetsUdp = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public static bool VerifierInstallationPiloteNpcap(out string messageDiagnostic)
    {
        if (File.Exists(CheminBibliothequeWpcap))
        {
            DefinirRepertoireBibliothequesDll(RepertoirePiloteNpcap);
            messageDiagnostic = $"Pilote Npcap détecté ({CheminBibliothequeWpcap})";
            return true;
        }

        messageDiagnostic = $"Pilote Npcap introuvable dans {CheminBibliothequeWpcap}. Veuillez installer Npcap (https://npcap.com/).";
        return false;
    }

    public int DemarrerCaptureReseau()
    {
        if (!VerifierInstallationPiloteNpcap(out string messageDiagnostic))
        {
            throw new InvalidOperationException(messageDiagnostic);
        }

        _sourceAnnulation = new CancellationTokenSource();
        _tacheConsommationCanal = Task.Run(() => ConsommerCanalPaquetsAsync(_sourceAnnulation.Token));

        var toutesLesCartesReseau = LibPcapLiveDeviceList.Instance;
        int nombreCartesOuvertes = 0;

        foreach (var carteReseau in toutesLesCartesReseau)
        {
            try
            {
                carteReseau.Open(new DeviceConfiguration
                {
                    Mode = DeviceModes.Promiscuous,
                    ReadTimeout = _configurationCapture.DelaiLectureMillisecondes
                });

                try
                {
                    carteReseau.Filter = _configurationCapture.FiltreCaptureBpf;
                }
                catch
                {
                    // Sur certaines interfaces virtuelles/VPN où le filtre BPF échoue, le filtrage par port est assuré en C#
                }

                carteReseau.OnPacketArrival += SurArriveePaquetReseau;
                carteReseau.StartCapture();
                _cartesReseauActives.Add(carteReseau);
                nombreCartesOuvertes++;

                string descriptionCarte = !string.IsNullOrWhiteSpace(carteReseau.Description)
                    ? carteReseau.Description
                    : carteReseau.Name;
                SurMessageStatutReseau?.Invoke($"[Npcap] Écoute active sur : {descriptionCarte}");
            }
            catch
            {
                // Ignorer les interfaces système inactives ou non autorisées
            }
        }

        return nombreCartesOuvertes;
    }

    private void SurArriveePaquetReseau(object expediteur, PacketCapture evenementCapture)
    {
        try
        {
            var captureBrute = evenementCapture.GetPacket();
            if (captureBrute.Data is null || captureBrute.Data.Length < 28)
                return;

            byte[]? donneesUdpAlbion = EssayerExtraireChargeUtileUdpAlbion(captureBrute);
            if (donneesUdpAlbion is { Length: >= 12 })
            {
                NombreTotalPaquetsUdpCaptures++;
                DateDernierPaquetRecuUtc = DateTime.UtcNow;

                if (expediteur is LibPcapLiveDevice carteActive && NomCarteReseauActive is null)
                {
                    NomCarteReseauActive = !string.IsNullOrWhiteSpace(carteActive.Description)
                        ? carteActive.Description
                        : carteActive.Name;
                }

                _canalPaquetsUdp.Writer.TryWrite(donneesUdpAlbion);
            }
        }
        catch
        {
            // Garantir qu'aucune trame réseau atypique n'interrompt le thread de capture Npcap
        }
    }

    /// <summary>
    /// Extrait la charge utile UDP d'une trame réseau brute si ses ports source/destination correspondent à Albion Online.
    /// Gère à la fois les trames Ethernet/Wi-Fi standard (via PacketDotNet) et les trames brutes TUN/VPN (ExitLag, WireGuard).
    /// </summary>
    private byte[]? EssayerExtraireChargeUtileUdpAlbion(RawCapture captureBrute)
    {
        try
        {
            var paquetAnalyse = Packet.ParsePacket(captureBrute.LinkLayerType, captureBrute.Data);
            var paquetUdp = paquetAnalyse.Extract<UdpPacket>();
            if (paquetUdp is not null)
            {
                if (_portsUdpAlbion.Contains(paquetUdp.SourcePort) || _portsUdpAlbion.Contains(paquetUdp.DestinationPort))
                {
                    byte[]? chargeUtile = paquetUdp.PayloadData;
                    if (chargeUtile is { Length: > 0 })
                        return chargeUtile;
                }
                return null;
            }
        }
        catch
        {
            // Fallback ci-dessous pour les interfaces TUN / VPN (ExitLag, WireGuard, etc.)
        }

        ReadOnlySpan<byte> octetsTrame = captureBrute.Data;
        for (int debutIp = 0; debutIp <= Math.Min(18, octetsTrame.Length - 28); debutIp++)
        {
            byte versionEtLongueurEntete = octetsTrame[debutIp];
            if ((versionEtLongueurEntete >> 4) != 4)
                continue;

            int tailleEnteteIpOctets = (versionEtLongueurEntete & 0x0F) * 4;
            if (tailleEnteteIpOctets < 20 || debutIp + tailleEnteteIpOctets + 8 > octetsTrame.Length)
                continue;

            byte codeProtocole = octetsTrame[debutIp + 9];
            if (codeProtocole != 17) // 17 = Protocole UDP
                continue;

            int decalageUdp = debutIp + tailleEnteteIpOctets;
            ushort portSource = BinaryPrimitives.ReadUInt16BigEndian(octetsTrame.Slice(decalageUdp, 2));
            ushort portDestination = BinaryPrimitives.ReadUInt16BigEndian(octetsTrame.Slice(decalageUdp + 2, 2));

            if (!_portsUdpAlbion.Contains(portSource) && !_portsUdpAlbion.Contains(portDestination))
                continue;

            ushort longueurTotaleUdp = BinaryPrimitives.ReadUInt16BigEndian(octetsTrame.Slice(decalageUdp + 4, 2));
            int decalageDonnees = decalageUdp + 8;
            int longueurDonnees = Math.Min(longueurTotaleUdp - 8, octetsTrame.Length - decalageDonnees);

            if (longueurDonnees >= 12)
                return octetsTrame.Slice(decalageDonnees, longueurDonnees).ToArray();
        }

        return null;
    }

    private async Task ConsommerCanalPaquetsAsync(CancellationToken jetonAnnulation)
    {
        await foreach (byte[] donneesUdp in _canalPaquetsUdp.Reader.ReadAllAsync(jetonAnnulation))
        {
            try
            {
                SurPaquetUdpCapture?.Invoke(donneesUdp);
            }
            catch
            {
            }
        }
    }

    public void ArreterCaptureReseau()
    {
        _sourceAnnulation?.Cancel();
        _canalPaquetsUdp.Writer.TryComplete();

        foreach (var carteReseau in _cartesReseauActives)
        {
            try
            {
                carteReseau.OnPacketArrival -= SurArriveePaquetReseau;
                carteReseau.StopCapture();
                carteReseau.Close();
            }
            catch
            {
            }
        }
        _cartesReseauActives.Clear();
    }

    public void Dispose()
    {
        ArreterCaptureReseau();
        _sourceAnnulation?.Dispose();
    }
}

