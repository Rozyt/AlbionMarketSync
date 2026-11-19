using System.Collections.Concurrent;
using System.Diagnostics;
using AlbionMarketSync.Application.Configuration;
using AlbionMarketSync.Application.Contrats;
using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Enumerations;
using AlbionMarketSync.Domain.Interfaces;
using AlbionMarketSync.Domain.ServicesMetier;

namespace AlbionMarketSync.Application.CasUtilisation;

/// <summary>
/// Coordinateur applicatif de synchronisation Google Sheets (Module 4) :
/// 1) Consolide le meilleur prix par cellule sur une fenêtre de 3 min (anti-écrasement Page 2+)
/// 2) Déduplique les prix inchangés via le cache local SQLite
/// 3) Regroupe les cellules modifiées pendant 2,5 secondes (Debounce)
/// 4) Déclenche l'écriture groupée via IPasserelleGoogleSheets.
/// </summary>
public sealed class CoordinateurSynchronisationGoogleSheets : IDisposable
{
    private readonly ConfigurationApplication _configurationApplication;
    private readonly IMoteurCartographieCellules _moteurCartographie;
    private readonly ICachePrixMarcheLocal _cachePrixLocal;
    private readonly IPasserelleGoogleSheets _passerelleGoogleSheets;
    private readonly IJournaliseurApplication _journaliseur;
    private readonly ConsolidateurMeilleurPrixSession _consolidateurPrixSession = new();
    private readonly ConcurrentDictionary<string, MiseAJourCelluleSheet> _misesAJourEnAttente = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _minuteurRegroupement;
    private readonly SemaphoreSlim _verrouEnvoiLot = new(1, 1);

    public StrategieCalculPrix StrategieActive { get; set; }
    public long NombreTotalCellulesSynchronisees { get; private set; }
    public long NombreTotalLotsEnvoyes { get; private set; }
    public long NombreTotalDoublonsIgnores { get; private set; }

    public event Action<IReadOnlyList<MiseAJourCelluleSheet>>? SurLotCellulesSynchronise;
    public event Action<IReadOnlyList<MiseAJourCelluleSheet>>? SurCellulesObserveesEnJeu;

    public CoordinateurSynchronisationGoogleSheets(
        ConfigurationApplication configurationApplication,
        IMoteurCartographieCellules moteurCartographie,
        ICachePrixMarcheLocal cachePrixLocal,
        IPasserelleGoogleSheets passerelleGoogleSheets,
        IJournaliseurApplication journaliseur)
    {
        _configurationApplication = configurationApplication;
        _moteurCartographie = moteurCartographie;
        _cachePrixLocal = cachePrixLocal;
        _passerelleGoogleSheets = passerelleGoogleSheets;
        _journaliseur = journaliseur;
        StrategieActive = configurationApplication.CalculPrix.ObtenirStrategieCalcul();
        _minuteurRegroupement = new Timer(SurEcheanceMinuteurRegroupement, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task InitialiserAsync(string repertoireTravail, CancellationToken jetonAnnulation = default)
    {
        await _passerelleGoogleSheets.InitialiserConnexionAsync(repertoireTravail, jetonAnnulation);
        _journaliseur.Succes($"  🟢 Mode Google Sheets actif : {_passerelleGoogleSheets.DescriptionModeAuthentification}");
        await ActualiserCartographieOngletSelectionAsync(jetonAnnulation);
    }

    public async Task ActualiserCartographieOngletSelectionAsync(CancellationToken jetonAnnulation = default)
    {
        try
        {
            var lignesGrille = await _passerelleGoogleSheets.LireGrilleOngletSelectionAsync(jetonAnnulation);
            if (lignesGrille is { Count: > 0 })
            {
                int nombreLignesActives = _moteurCartographie.CartographeSelection.ChargerDepuisGrilleSheet(lignesGrille);
                _journaliseur.MettreEnEvidence($"  📋 [Module 3.2 'Sélection'] {nombreLignesActives} ligne(s) d'équipements actifs chargées depuis le classeur :");
                foreach (var equipement in _moteurCartographie.CartographeSelection.LignesEquipementsActives)
                {
                    _journaliseur.MettreEnEvidence($"     • {equipement.Categorie,-13} : '{equipement.NomBrutSaisi}' (T{equipement.PalierTier}.{equipement.NiveauEnchantement}) -> {equipement.CoordonneeCellulePrixVente} ({equipement.CleBaseAlbion})");
                }
            }
        }
        catch (Exception erreur)
        {
            _journaliseur.Avertir($"  ⚠️ Impossible de lire l'onglet 'Sélection' : {erreur.Message}");
        }
    }

    /// <summary>
    /// Traite les résumés de prix extraits d'une page d'Hôtel des Ventes :
    /// archive l'historique SQLite, consolide le meilleur prix de la session, filtre les doublons
    /// et planifie l'envoi groupé (debounce 2,5 s) vers Google Sheets.
    /// </summary>
    public void TraiterResumesArticlesMarche(IReadOnlyList<ResumeArticleMarche> listeResumesArticles)
    {
        bool nouvelleCelluleMiseEnFile = false;
        DateTime instantActuelUtc = DateTime.UtcNow;
        var cellulesObserveesDansPage = new List<MiseAJourCelluleSheet>();

        foreach (var resumeArticle in listeResumesArticles)
        {
            _cachePrixLocal.EnregistrerHistoriqueMarche(
                resumeArticle.IdentifiantObjet,
                resumeArticle.PalierTier,
                resumeArticle.NiveauEnchantement,
                resumeArticle.NiveauQualite,
                resumeArticle.TypeEnchere,
                resumeArticle.PrixUnitaireMinimumArgent,
                resumeArticle.PrixMoyenPondereArgent,
                resumeArticle.QuantiteTotaleDisponible,
                resumeArticle.VilleMarche,
                resumeArticle.CoordonneeCelluleSheetCible);

            if (!string.Equals(resumeArticle.TypeEnchere, "offer", StringComparison.OrdinalIgnoreCase))
                continue;

            var cellulesCibles = _moteurCartographie.ResoudreToutesLesCellulesCibles(
                resumeArticle.IdentifiantObjet,
                resumeArticle.PalierTier,
                resumeArticle.NiveauEnchantement);

            if (cellulesCibles.Count == 0)
                continue;

            double prixCandidatArrondi = CalculateurPrixMarche.SelectionnerPrixReferenceArrondi(resumeArticle, StrategieActive);
            if (prixCandidatArrondi <= 0)
                continue;

            foreach (string coordonneeCellule in cellulesCibles)
            {
                if (GardeFouProtectionFormulesSheet.EstCelluleProtegee(coordonneeCellule))
                    continue;

                double meilleurPrixConsolide = _consolidateurPrixSession.ConsoliderMeilleurPrix(
                    coordonneeCellule,
                    prixCandidatArrondi,
                    instantActuelUtc);

                cellulesObserveesDansPage.Add(new MiseAJourCelluleSheet(
                    CoordonneeCellule: coordonneeCellule,
                    IdentifiantObjet: resumeArticle.IdentifiantObjet,
                    NomFrancaisObjet: resumeArticle.NomFrancaisObjet,
                    PrixArgentArrondi: meilleurPrixConsolide,
                    VilleMarche: resumeArticle.VilleMarche,
                    DateMiseEnFileUtc: instantActuelUtc));

                if (!_cachePrixLocal.LePrixDeLaCelluleAChange(coordonneeCellule, meilleurPrixConsolide, out double? ancienPrixEnCache))
                {
                    if (_misesAJourEnAttente.TryGetValue(coordonneeCellule, out var miseAJourEnFile) &&
                        meilleurPrixConsolide < miseAJourEnFile.PrixArgentArrondi)
                    {
                        _misesAJourEnAttente[coordonneeCellule] = miseAJourEnFile with
                        {
                            PrixArgentArrondi = meilleurPrixConsolide,
                            DateMiseEnFileUtc = instantActuelUtc
                        };
                        nouvelleCelluleMiseEnFile = true;
                    }
                    else
                    {
                        NombreTotalDoublonsIgnores++;
                    }
                    continue;
                }

                if (_misesAJourEnAttente.TryGetValue(coordonneeCellule, out var miseAJourActuelle))
                {
                    if (Math.Abs(miseAJourActuelle.PrixArgentArrondi - meilleurPrixConsolide) < 0.5)
                        continue;

                    if (meilleurPrixConsolide > miseAJourActuelle.PrixArgentArrondi)
                        continue;
                }

                _misesAJourEnAttente[coordonneeCellule] = new MiseAJourCelluleSheet(
                    CoordonneeCellule: coordonneeCellule,
                    IdentifiantObjet: resumeArticle.IdentifiantObjet,
                    NomFrancaisObjet: resumeArticle.NomFrancaisObjet,
                    PrixArgentArrondi: meilleurPrixConsolide,
                    VilleMarche: resumeArticle.VilleMarche,
                    DateMiseEnFileUtc: instantActuelUtc);

                string detailAncienPrix = ancienPrixEnCache.HasValue ? $" (ancien: {ancienPrixEnCache.Value:N0})" : " (nouveau)";
                _journaliseur.MettreEnEvidence($"  ⏳ [File Batch] {coordonneeCellule,-20} <= {meilleurPrixConsolide,10:N0} Ag{detailAncienPrix} [{resumeArticle.NomFrancaisObjet}]");
                nouvelleCelluleMiseEnFile = true;
            }
        }

        if (cellulesObserveesDansPage.Count > 0)
        {
            SurCellulesObserveesEnJeu?.Invoke(cellulesObserveesDansPage);
        }

        if (nouvelleCelluleMiseEnFile && _configurationApplication.Synchronisation.ActiverEnvoiGoogleSheets)
        {
            int delaiRegroupementMs = Math.Clamp(_configurationApplication.Synchronisation.FenetreRegroupementMillisecondes, 500, 15000);
            _minuteurRegroupement.Change(delaiRegroupementMs, Timeout.Infinite);
        }
    }

    private void SurEcheanceMinuteurRegroupement(object? etat)
    {
        _ = ViderFileMisesAJourSansExceptionAsync();
    }

    private async Task ViderFileMisesAJourSansExceptionAsync()
    {
        try
        {
            await ViderFileMisesAJourEnAttenteAsync();
        }
        catch (Exception erreur)
        {
            _journaliseur.Erreur($"  ❌ Erreur lors de l'envoi Google Sheets : {erreur.Message}");
        }
    }

    public async Task ViderFileMisesAJourEnAttenteAsync(CancellationToken jetonAnnulation = default)
    {
        if (_misesAJourEnAttente.IsEmpty || !_configurationApplication.Synchronisation.ActiverEnvoiGoogleSheets)
            return;

        await _verrouEnvoiLot.WaitAsync(jetonAnnulation);
        try
        {
            var lotAEnvoyer = new List<MiseAJourCelluleSheet>();
            foreach (var elementEnFile in _misesAJourEnAttente)
            {
                if (_misesAJourEnAttente.TryRemove(elementEnFile.Key, out var miseAJourRetiree))
                {
                    if (!GardeFouProtectionFormulesSheet.EstCelluleProtegee(miseAJourRetiree.CoordonneeCellule))
                        lotAEnvoyer.Add(miseAJourRetiree);
                }
            }

            if (lotAEnvoyer.Count == 0)
                return;

            var chronometre = Stopwatch.StartNew();
            await _passerelleGoogleSheets.EnvoyerLotMisesAJourCellulesAsync(lotAEnvoyer, jetonAnnulation);
            chronometre.Stop();

            foreach (var miseAJour in lotAEnvoyer)
            {
                _cachePrixLocal.EnregistrerPrixCelluleSynchronise(
                    miseAJour.CoordonneeCellule,
                    miseAJour.IdentifiantObjet,
                    miseAJour.PrixArgentArrondi,
                    miseAJour.VilleMarche);
            }

            NombreTotalLotsEnvoyes++;
            NombreTotalCellulesSynchronisees += lotAEnvoyer.Count;

            string resumeCellules = string.Join(", ", lotAEnvoyer.Select(cellule => $"{cellule.CoordonneeCellule}={cellule.PrixArgentArrondi:N0}"));
            string etiquetteMode = _passerelleGoogleSheets.EstConnecteEnDirectApiGoogle ? "GOOGLE SHEETS API v4" : "BATCH SPOOLER + SQLITE";
            _journaliseur.MettreEnEvidence($"  🚀 [{etiquetteMode} OK en {chronometre.ElapsedMilliseconds} ms] {lotAEnvoyer.Count} cellule(s) consolidée(s) : {resumeCellules}");

            SurLotCellulesSynchronise?.Invoke(lotAEnvoyer);
        }
        catch (Exception erreur)
        {
            _journaliseur.Erreur($"  ❌ Échec synchronisation lot Google Sheets : {erreur.Message}");
        }
        finally
        {
            _verrouEnvoiLot.Release();
        }
    }

    public void Dispose()
    {
        _minuteurRegroupement.Dispose();
        _verrouEnvoiLot.Dispose();
    }
}

