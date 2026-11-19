using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Enumerations;

namespace AlbionMarketSync.Application.Contrats;

/// <summary>
/// Contrat de l'orchestrateur central d'AlbionMarketSync.
/// Pilote toute la chaîne (Capture Réseau -> Décodage Photon -> Analyse Marché -> Synchronisation Google Sheets).
/// </summary>
public interface IOrchestrateurSynchronisationMarche : IDisposable
{
    EtatSanteCaptureReseau EtatSanteCapture { get; }
    string VilleActive { get; set; }
    StrategieCalculPrix StrategiePrixActive { get; set; }
    long NombreTotalPaquetsUdp { get; }
    long NombreTotalPagesMarcheDecodees { get; }
    long NombreTotalCellulesSynchronisees { get; }
    long NombreTotalDoublonsIgnores { get; }

    event Action<IReadOnlyList<ResumeArticleMarche>, IReadOnlyList<OrdreMarcheAlbion>>? SurLotMarcheTraite;
    event Action<string, string>? SurChangementVilleDetecte;

    Task InitialiserAsync(string repertoireTravail, CancellationToken jetonAnnulation = default);
    int DemarrerCaptureEnDirect();
    Task ArreterEtViderFileAsync(CancellationToken jetonAnnulation = default);
}

