using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Interfaces;
using AlbionMarketSync.Domain.ServicesMetier;

namespace AlbionMarketSync.Application.CasUtilisation;

/// <summary>
/// Cas d'utilisation qui pilote le guidage pas-à-pas du joueur dans l'Hôtel des Ventes :
/// - Enregistre le choix des Capes, du Niveau (Tier) Max et de l'Enchantement Max
/// - À chaque envoi vers Google Sheets, inspecte la feuille 'Indice prix'
/// - Calcule et expose la prochaine requête exacte à effectuer.
/// </summary>
public sealed class AssistantGuidageCraftCape
{
    private readonly IPasserelleGoogleSheets _passerelleGoogleSheets;
    private readonly ICachePrixMarcheLocal _cachePrixLocal;
    private readonly HashSet<string> _cellulesActualiseesDurantSession = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _cellulesIgnoreesManuellement = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<IReadOnlyList<string>> _derniereGrilleIndicePrix = Array.Empty<string[]>();

    public List<RecetteCraftCape> CapesSelectionnees { get; } =
    [
        PlanificateurRequetesCraftCape.CatalogueCapesDisponibles[0] // Par défaut : Cape de Lymhurst
    ];

    public int PalierTierMinimum { get; set; } = 4;
    public int PalierTierMaximum { get; set; } = 6;
    public int EnchantementMaximum { get; set; } = 2;
    public bool ExigerActualisationSession { get; set; } = false;

    public IReadOnlyList<EtapeRechercheHotelDesVentes> PlanRequetesActuel { get; private set; } = Array.Empty<EtapeRechercheHotelDesVentes>();
    public EtapeRechercheHotelDesVentes? ProchaineRequeteAFaire { get; private set; }

    public int NombreEtapesValidees => PlanRequetesActuel.Count(etape =>
        etape.EstIgnoreeManuellement ||
        (ExigerActualisationSession ? etape.EstActualiseeDurantSession : etape.EstRemplieDansIndicePrix));

    public int NombreEtapesTotales => PlanRequetesActuel.Count;

    public event Action? SurMiseAJourGuidage;

    public AssistantGuidageCraftCape(
        IPasserelleGoogleSheets passerelleGoogleSheets,
        ICachePrixMarcheLocal cachePrixLocal,
        CoordinateurSynchronisationGoogleSheets coordinateurSynchronisation)
    {
        _passerelleGoogleSheets = passerelleGoogleSheets;
        _cachePrixLocal = cachePrixLocal;
        coordinateurSynchronisation.SurLotCellulesSynchronise += SurLotSynchroniseVersSheet;
        coordinateurSynchronisation.SurCellulesObserveesEnJeu += SurCellulesObserveesEnJeu;
    }

    /// <summary>
    /// Relit l'onglet 'Indice prix' depuis Google Sheets, synchronise le cache SQLite local
    /// (purge des cellules remises à zéro) et recalcule la prochaine étape à chercher.
    /// </summary>
    public async Task ActualiserDepuisFeuilleIndicePrixAsync(CancellationToken jetonAnnulation = default)
    {
        _derniereGrilleIndicePrix = await _passerelleGoogleSheets.LireGrilleIndicePrixAsync(jetonAnnulation);
        _cachePrixLocal.SynchroniserCacheAvecFeuilleIndicePrix(_derniereGrilleIndicePrix);
        RecalculerPlanEtProchaineRequete();
    }

    /// <summary>
    /// Recalcule la liste ordonnée des ingrédients requis et sélectionne la prochaine requête à afficher au joueur.
    /// </summary>
    public void RecalculerPlanEtProchaineRequete()
    {
        PlanRequetesActuel = PlanificateurRequetesCraftCape.ConstruirePlanRequetes(
            CapesSelectionnees,
            PalierTierMinimum,
            PalierTierMaximum,
            EnchantementMaximum,
            _derniereGrilleIndicePrix,
            _cellulesActualiseesDurantSession,
            _cellulesIgnoreesManuellement);

        ProchaineRequeteAFaire = PlanificateurRequetesCraftCape.DeterminerProchaineRequete(
            PlanRequetesActuel,
            ExigerActualisationSession);

        SurMiseAJourGuidage?.Invoke();
    }

    public void IgnorerProchaineEtape()
    {
        if (ProchaineRequeteAFaire is not null)
        {
            _cellulesIgnoreesManuellement.Add(ProchaineRequeteAFaire.CoordonneeCelluleIndicePrix);
            RecalculerPlanEtProchaineRequete();
        }
    }

    public void ReinitialiserProgressionSession()
    {
        _cellulesActualiseesDurantSession.Clear();
        _cellulesIgnoreesManuellement.Clear();
        RecalculerPlanEtProchaineRequete();
    }

    private void SurLotSynchroniseVersSheet(IReadOnlyList<MiseAJourCelluleSheet> lotSynchronise)
    {
        foreach (var miseAJour in lotSynchronise)
        {
            _cellulesActualiseesDurantSession.Add(miseAJour.CoordonneeCellule);
        }

        _ = ActualiserDepuisFeuilleIndicePrixAsync();
    }

    private void SurCellulesObserveesEnJeu(IReadOnlyList<MiseAJourCelluleSheet> cellulesObservees)
    {
        bool changementSession = false;
        foreach (var cellule in cellulesObservees)
        {
            if (_cellulesActualiseesDurantSession.Add(cellule.CoordonneeCellule))
                changementSession = true;
        }

        if (changementSession)
        {
            RecalculerPlanEtProchaineRequete();
        }
    }
}
