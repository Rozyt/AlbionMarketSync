using System.Text;
using System.Text.Json;
using AlbionMarketSync.Application.Configuration;
using AlbionMarketSync.Application.Contrats;
using AlbionMarketSync.Application.CasUtilisation;
using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Interfaces;
using AlbionMarketSync.Infrastructure.Reseau;
using AlbionMarketSync.Infrastructure.Serialisation;

namespace AlbionMarketSync.Presentation;

/// <summary>
/// Contrôleur de la couche Présentation en ligne de commande (Console / Probe / Replay).
/// Affiche les résultats en temps réel et pilote l'orchestrateur applicatif.
/// </summary>
public sealed class ControleurLigneDeCommande(
    ConfigurationApplication configurationApplication,
    ConfigurationCodesOperations configurationCodesOperations,
    string cheminFichierCodesOperations,
    string repertoireTravail,
    IMoteurCartographieCellules moteurCartographie,
    ICachePrixMarcheLocal cachePrixLocal,
    IServiceCapturePaquetsReseau serviceCaptureReseau,
    IAnalyseurPaquetsPhoton analyseurPaquetsPhoton,
    ProcesseurPaquetsMarcheAlbion processeurMarche,
    CoordinateurSynchronisationGoogleSheets coordinateurSynchronisation,
    IOrchestrateurSynchronisationMarche orchestrateurSynchronisation,
    AssistantGuidageCraftCape assistantGuidage,
    IJournaliseurApplication journaliseur)
{
    private readonly string _cheminJournalDonneesCapturees = Path.Combine(repertoireTravail, "Infrastructure", "Logs", "captured_market_data.jsonl");
    private bool _premierPaquetAlbionSignale;

    /// <summary>
    /// Point d'entrée du mode Console : initialise le cache SQLite, la connexion Google Sheets,
    /// puis lance soit la relecture d'un historique (--replay-captured), soit l'écoute réseau en direct.
    /// </summary>
    public async Task<int> ExecuterAsync(string[] argumentsLigneDeCommande, CancellationToken jetonAnnulation)
    {
        AfficherBanniereDemarrage();

        journaliseur.Informer($"\n[Initialisation Module 4 : Cache Local SQLite & Connexion Google Sheets]");
        journaliseur.Succes($"  🟢 Base SQLite WAL prête ({configurationApplication.CheminBaseCacheSqlite} — {cachePrixLocal.ObtenirNombreCellulesEnCache()} cellule(s) en cache)");
        await orchestrateurSynchronisation.InitialiserAsync(repertoireTravail, jetonAnnulation);
        await assistantGuidage.ActualiserDepuisFeuilleIndicePrixAsync(jetonAnnulation);
        AfficherProchaineRequeteGuidage();

        assistantGuidage.SurMiseAJourGuidage += AfficherProchaineRequeteGuidage;

        ExecuterDiagnosticCartographie();

        if (argumentsLigneDeCommande.Length >= 1 &&
            string.Equals(argumentsLigneDeCommande[0], "--replay-captured", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuterModeRelectureHistoriqueAsync(jetonAnnulation);
            return 0;
        }

        return await ExecuterModeCaptureEnDirectAsync(argumentsLigneDeCommande, jetonAnnulation);
    }

    private void AfficherBanniereDemarrage()
    {
        journaliseur.MettreEnEvidence("====================================================================================");
        journaliseur.MettreEnEvidence("  ALBION MARKET SYNC v2.0 — ARCHITECTURE PROPRE [.NET 9 / C# 13]");
        journaliseur.MettreEnEvidence("  Domain | Application | Infrastructure | Presentation");
        journaliseur.MettreEnEvidence("====================================================================================");
    }

    private void ExecuterDiagnosticCartographie()
    {
        journaliseur.Informer("\n[Auto-Test Module 3 : Cartographie Cellulaire 'Indice prix' + 'Sélection']");
        string[] objetsDeVerification =
        [
            "T2_LEATHER",
            "T4_LEATHER",
            "T8_METALBAR_LEVEL4@4",
            "T6_CAPEITEM_FW_LYMHURST_BP",
            "T1_FACTION_FOREST_TOKEN_1",
            "QUESTITEM_TOKEN_ROYAL_T6",
            "T7_ARTEFACT_ARMOR_LEATHER_MORGANA",
            "T4_CAPEITEM_AVALON"
        ];

        foreach (string identifiantObjet in objetsDeVerification)
        {
            string? celluleCible = moteurCartographie.ResoudreCelluleCible(identifiantObjet, 0, 0);
            string nomFrancais = moteurCartographie.ResoudreNomFrancais(identifiantObjet, 0, 0);
            journaliseur.Informer($"  ✓ {identifiantObjet,-36} -> {celluleCible ?? "(Non listé)",-22} ({nomFrancais})");
        }
    }

    private async Task ExecuterModeRelectureHistoriqueAsync(CancellationToken jetonAnnulation)
    {
        journaliseur.Avertir("\n[Mode Replay : Traitement de captured_market_data.jsonl vers SQLite & Google Sheets]");
        if (!File.Exists(_cheminJournalDonneesCapturees))
        {
            journaliseur.Avertir("  ⚠️ Aucun fichier captured_market_data.jsonl trouvé.");
            return;
        }

        string[] lignesJson = await File.ReadAllLinesAsync(_cheminJournalDonneesCapturees, jetonAnnulation);
        var lotResumes = new List<ResumeArticleMarche>();
        foreach (string ligne in lignesJson)
        {
            if (string.IsNullOrWhiteSpace(ligne))
                continue;

            var resumeArticle = JsonSerializer.Deserialize(ligne, ContexteSerialisationJsonAlbion.Default.ResumeArticleMarche);
            if (resumeArticle is not null)
            {
                string? celluleMiseAJour = moteurCartographie.ResoudreCelluleCible(
                    resumeArticle.IdentifiantObjet,
                    resumeArticle.PalierTier,
                    resumeArticle.NiveauEnchantement);
                lotResumes.Add(resumeArticle with { CoordonneeCelluleSheetCible = celluleMiseAJour });
            }
        }

        coordinateurSynchronisation.TraiterResumesArticlesMarche(lotResumes);
        await coordinateurSynchronisation.ViderFileMisesAJourEnAttenteAsync(jetonAnnulation);
        journaliseur.Succes($"\n[Bilan Replay] {lotResumes.Count} entrées analysées | {coordinateurSynchronisation.NombreTotalCellulesSynchronisees} cellule(s) synchronisée(s) | {coordinateurSynchronisation.NombreTotalDoublonsIgnores} doublon(s) ignoré(s) par SQLite.");
    }

    private async Task<int> ExecuterModeCaptureEnDirectAsync(string[] argumentsLigneDeCommande, CancellationToken jetonAnnulationExterne)
    {
        journaliseur.Informer("\n[Initialisation Module 1 : Pilote Réseau Npcap]");
        if (!ServiceCaptureReseauNpcap.VerifierInstallationPiloteNpcap(out string messageDiagnosticNpcap))
        {
            journaliseur.Erreur($"  🔴 {messageDiagnosticNpcap}");
            return 1;
        }

        journaliseur.Succes($"  🟢 {messageDiagnosticNpcap}");

        serviceCaptureReseau.SurMessageStatutReseau += message => journaliseur.Informer($"  {message}");

        analyseurPaquetsPhoton.SurMessagePhotonDecode += _ =>
        {
            if (!_premierPaquetAlbionSignale)
            {
                _premierPaquetAlbionSignale = true;
                journaliseur.Succes($"\n  🟢 [{DateTime.Now:HH:mm:ss}] FLUX ALBION ONLINE DÉTECTÉ SUR LA CARTE RÉSEAU : {serviceCaptureReseau.NomCarteReseauActive}");
                journaliseur.Succes("     -> Décodeur Photon Protocol18/16 synchronisé ! Ouvrez l'Hôtel des Ventes (HO) en jeu.");
            }
        };

        orchestrateurSynchronisation.SurChangementVilleDetecte += (nouvelleVille, identifiantCluster) =>
        {
            journaliseur.Avertir($"\n  🏙️ [{DateTime.Now:HH:mm:ss}] VILLE ACTIVE DÉTECTÉE : {nouvelleVille} (Cluster ID: {identifiantCluster})");
        };

        processeurMarche.SurCodeOperationDecouvert += (nomOperation, nouveauCode) =>
        {
            journaliseur.MettreEnEvidence($"\n  🔍 [{DateTime.Now:HH:mm:ss}] DÉCOUVERTE AUTOMATIQUE PATCH ALBION : {nomOperation} = Code {nouveauCode}");
            try
            {
                string jsonMisAJour = JsonSerializer.Serialize(configurationCodesOperations, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(cheminFichierCodesOperations, jsonMisAJour, Encoding.UTF8);
            }
            catch
            {
            }
        };

        orchestrateurSynchronisation.SurLotMarcheTraite += AfficherTableauPageMarcheCapturee;

        int nombreCartesActives = orchestrateurSynchronisation.DemarrerCaptureEnDirect();
        journaliseur.Succes($"  🟢 Écoute BPF ('{configurationApplication.CaptureReseau.FiltreCaptureBpf}') démarrée sur {nombreCartesActives} interface(s) réseau.");
        journaliseur.Avertir("  🟡 En attente de paquets Albion Online... (Ouvrez l'Hôtel des Ventes en jeu)");

        int? dureeSondeSecondes = null;
        if (argumentsLigneDeCommande.Length >= 2 &&
            string.Equals(argumentsLigneDeCommande[0], "--probe", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(argumentsLigneDeCommande[1], out int secondesAnalyses))
        {
            dureeSondeSecondes = secondesAnalyses;
        }

        using var sourceAnnulationLiee = CancellationTokenSource.CreateLinkedTokenSource(jetonAnnulationExterne);
        if (dureeSondeSecondes.HasValue)
            sourceAnnulationLiee.CancelAfter(TimeSpan.FromSeconds(dureeSondeSecondes.Value));

        while (!sourceAnnulationLiee.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(3000, sourceAnnulationLiee.Token);
                if (serviceCaptureReseau.NombreTotalPaquetsUdpCaptures > 0 && !_premierPaquetAlbionSignale)
                {
                    _premierPaquetAlbionSignale = true;
                    journaliseur.Succes($"\n  🟢 [{DateTime.Now:HH:mm:ss}] TRAFIC UDP ALBION DÉTECTÉ ({serviceCaptureReseau.NombreTotalPaquetsUdpCaptures} paquets sur {serviceCaptureReseau.NomCarteReseauActive})");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await orchestrateurSynchronisation.ArreterEtViderFileAsync(CancellationToken.None);
        journaliseur.Informer($"\n[Bilan Capture] Paquets UDP: {serviceCaptureReseau.NombreTotalPaquetsUdpCaptures} | Paquets Photon: {analyseurPaquetsPhoton.NombreTotalPaquetsPhoton} | Commandes: {analyseurPaquetsPhoton.NombreTotalCommandesTraitees} | Fragments réassemblés: {analyseurPaquetsPhoton.NombreTotalMessagesFragmentesReassembles} | Messages décodés: {analyseurPaquetsPhoton.NombreTotalMessagesDecodes} | Pages HV: {processeurMarche.NombreTotalPagesMarcheDecodees} | Cellules synchronisées: {coordinateurSynchronisation.NombreTotalCellulesSynchronisees}");
        return 0;
    }

    private void AfficherTableauPageMarcheCapturee(
        IReadOnlyList<ResumeArticleMarche> resumesArticles,
        IReadOnlyList<OrdreMarcheAlbion> ordresBruts)
    {
        journaliseur.MettreEnEvidence($"\n====================================================================================");
        journaliseur.MettreEnEvidence($"  📦 [{DateTime.Now:HH:mm:ss}] PAGE HÔTEL DES VENTES CAPTURÉE ({ordresBruts.Count} ordres bruts | Ville: {orchestrateurSynchronisation.VilleActive})");
        journaliseur.MettreEnEvidence($"====================================================================================");
        journaliseur.Informer($"  {"OBJET (NOM & ID)",-42} | {"QUAL",-4} | {"TYPE",-7} | {"QTÉ",-6} | {"PRIX MIN (Ag)",-14} | {"MOY. POND.",-12} | {"CELLULE CIBLE"}");
        journaliseur.Informer($"  {new string('-', 118)}");

        foreach (var resume in resumesArticles)
        {
            string libelleObjet = $"{resume.NomFrancaisObjet} ({resume.IdentifiantObjet})";
            if (libelleObjet.Length > 42)
                libelleObjet = libelleObjet[..39] + "...";

            string libelleType = resume.TypeEnchere == "offer" ? "Vente" : "Achat";
            string libelleCellule = resume.CoordonneeCelluleSheetCible ?? "(Hors 'Indice prix' / Sélection)";

            var niveauSeverite = resume.CoordonneeCelluleSheetCible is not null
                ? NiveauSeveriteJournal.Succes
                : NiveauSeveriteJournal.Information;

            journaliseur.Journaliser(
                $"  {libelleObjet,-42} | Q{resume.NiveauQualite,-3} | {libelleType,-7} | {resume.QuantiteTotaleDisponible,6} | {resume.PrixUnitaireMinimumArgent,14:N0} | {resume.PrixMoyenPondereArgent,12:N0} | {libelleCellule}",
                niveauSeverite);

            string ligneJson = JsonSerializer.Serialize(resume, ContexteSerialisationJsonAlbion.Default.ResumeArticleMarche);
            File.AppendAllText(_cheminJournalDonneesCapturees, ligneJson + Environment.NewLine);
        }
    }

    private void AfficherProchaineRequeteGuidage()
    {
        var prochaine = assistantGuidage.ProchaineRequeteAFaire;
        int validees = assistantGuidage.NombreEtapesValidees;
        int total = assistantGuidage.NombreEtapesTotales;

        if (prochaine is not null)
        {
            journaliseur.MettreEnEvidence($"\n  🎯 [ASSISTANT CRAFT CAPES ({validees}/{total})] PROCHAINE REQUÊTE À FAIRE DANS L'HV :");
            journaliseur.Succes($"     👉 Objet : « {prochaine.NomObjetAChercherEnJeu} » | Tier : {prochaine.FiltrePalierTier} | Enchantement : {prochaine.FiltreEnchantement} (Cellule : {prochaine.CoordonneeCelluleIndicePrix})");
        }
        else if (total > 0)
        {
            journaliseur.Succes($"\n  🎉 [ASSISTANT CRAFT CAPES ({validees}/{total})] Toutes les cellules requises dans 'Indice prix' sont complètes !");
        }
    }
}

