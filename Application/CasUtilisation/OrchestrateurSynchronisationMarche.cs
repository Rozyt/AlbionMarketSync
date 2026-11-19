using AlbionMarketSync.Application.Contrats;
using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Enumerations;
using AlbionMarketSync.Domain.Interfaces;

namespace AlbionMarketSync.Application.CasUtilisation;

/// <summary>
/// Orchestrateur central implémentant IOrchestrateurSynchronisationMarche.
/// Relie la capture réseau Npcap, l'analyseur Photon, le processeur de marché et le coordinateur Google Sheets.
/// </summary>
public sealed class OrchestrateurSynchronisationMarche : IOrchestrateurSynchronisationMarche
{
    private readonly IServiceCapturePaquetsReseau _serviceCaptureReseau;
    private readonly IAnalyseurPaquetsPhoton _analyseurPaquetsPhoton;
    private readonly ProcesseurPaquetsMarcheAlbion _processeurMarche;
    private readonly CoordinateurSynchronisationGoogleSheets _coordinateurSynchronisation;

    public EtatSanteCaptureReseau EtatSanteCapture => _serviceCaptureReseau.EtatSanteCapture;

    public string VilleActive
    {
        get => _processeurMarche.VilleActuelle;
        set => _processeurMarche.VilleActuelle = value;
    }

    public StrategieCalculPrix StrategiePrixActive
    {
        get => _coordinateurSynchronisation.StrategieActive;
        set => _coordinateurSynchronisation.StrategieActive = value;
    }

    public long NombreTotalPaquetsUdp => _serviceCaptureReseau.NombreTotalPaquetsUdpCaptures;
    public long NombreTotalPagesMarcheDecodees => _processeurMarche.NombreTotalPagesMarcheDecodees;
    public long NombreTotalCellulesSynchronisees => _coordinateurSynchronisation.NombreTotalCellulesSynchronisees;
    public long NombreTotalDoublonsIgnores => _coordinateurSynchronisation.NombreTotalDoublonsIgnores;

    public event Action<IReadOnlyList<ResumeArticleMarche>, IReadOnlyList<OrdreMarcheAlbion>>? SurLotMarcheTraite;
    public event Action<string, string>? SurChangementVilleDetecte;

    public OrchestrateurSynchronisationMarche(
        IServiceCapturePaquetsReseau serviceCaptureReseau,
        IAnalyseurPaquetsPhoton analyseurPaquetsPhoton,
        ProcesseurPaquetsMarcheAlbion processeurMarche,
        CoordinateurSynchronisationGoogleSheets coordinateurSynchronisation)
    {
        _serviceCaptureReseau = serviceCaptureReseau;
        _analyseurPaquetsPhoton = analyseurPaquetsPhoton;
        _processeurMarche = processeurMarche;
        _coordinateurSynchronisation = coordinateurSynchronisation;

        _serviceCaptureReseau.SurPaquetUdpCapture += TransmettrePaquetUdpAuDecodeurPhoton;
        _analyseurPaquetsPhoton.SurMessagePhotonDecode += _processeurMarche.TraiterMessagePhoton;
        _processeurMarche.SurVilleDetectee += RelayerChangementVille;
        _processeurMarche.SurOrdresMarcheExtraits += TraiterEtSynchroniserOrdresExtraits;
    }

    public Task InitialiserAsync(string repertoireTravail, CancellationToken jetonAnnulation = default)
        => _coordinateurSynchronisation.InitialiserAsync(repertoireTravail, jetonAnnulation);

    public int DemarrerCaptureEnDirect()
        => _serviceCaptureReseau.DemarrerCaptureReseau();

    public async Task ArreterEtViderFileAsync(CancellationToken jetonAnnulation = default)
    {
        _serviceCaptureReseau.ArreterCaptureReseau();
        await _coordinateurSynchronisation.ViderFileMisesAJourEnAttenteAsync(jetonAnnulation);
    }

    private void TransmettrePaquetUdpAuDecodeurPhoton(byte[] donneesUdpBrutes)
        => _analyseurPaquetsPhoton.TraiterChargeUtileUdp(donneesUdpBrutes);

    private void RelayerChangementVille(string nouvelleVille, string identifiantCluster)
        => SurChangementVilleDetecte?.Invoke(nouvelleVille, identifiantCluster);

    private void TraiterEtSynchroniserOrdresExtraits(
        IReadOnlyList<ResumeArticleMarche> resumesArticles,
        IReadOnlyList<OrdreMarcheAlbion> ordresBruts)
    {
        SurLotMarcheTraite?.Invoke(resumesArticles, ordresBruts);
        _coordinateurSynchronisation.TraiterResumesArticlesMarche(resumesArticles);
    }

    public void Dispose()
    {
        _serviceCaptureReseau.SurPaquetUdpCapture -= TransmettrePaquetUdpAuDecodeurPhoton;
        _analyseurPaquetsPhoton.SurMessagePhotonDecode -= _processeurMarche.TraiterMessagePhoton;
        _processeurMarche.SurVilleDetectee -= RelayerChangementVille;
        _processeurMarche.SurOrdresMarcheExtraits -= TraiterEtSynchroniserOrdresExtraits;

        _serviceCaptureReseau.Dispose();
        _coordinateurSynchronisation.Dispose();
    }
}

