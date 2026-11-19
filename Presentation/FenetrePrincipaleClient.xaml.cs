using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AlbionMarketSync.Application.CasUtilisation;
using AlbionMarketSync.Application.Configuration;
using AlbionMarketSync.Application.Contrats;
using AlbionMarketSync.Domain.Enumerations;
using AlbionMarketSync.Domain.Interfaces;
using AlbionMarketSync.Domain.ServicesMetier;
using AlbionMarketSync.Infrastructure.GoogleSheets;
using AlbionMarketSync.Infrastructure.Journalisation;
using AlbionMarketSync.Infrastructure.Reseau;
using Microsoft.Win32;

namespace AlbionMarketSync.Presentation;

/// <summary>
/// Fenêtre cliente principale WPF permettant au joueur :
/// 1) De se connecter avec son compte Google (OAuth 2.0 navigateur ou clé JSON) et de choisir son Google Sheet
/// 2) De sélectionner les Capes à crafter, le Niveau (Tier) Max et l'Enchantement Max
/// 3) De voir en temps réel la prochaine requête exacte à faire dans l'Hôtel des Ventes basée sur la feuille 'Indice prix'.
/// </summary>
public partial class FenetrePrincipaleClient : Window
{
    private readonly IPasserelleGoogleSheets _passerelleGoogleSheets;
    private readonly IServiceCapturePaquetsReseau _serviceCaptureReseau;
    private readonly IOrchestrateurSynchronisationMarche _orchestrateurSynchronisation;
    private readonly AssistantGuidageCraftCape _assistantGuidage;
    private readonly JournaliseurConsoleEtFichier _journaliseur;
    private readonly string _repertoireTravail;
    private readonly ObservableCollection<ElementSelectionCape> _elementsCapes = new();
    private readonly DispatcherTimer _minuteurRafraichissementUi;
    private bool _initialisationTerminee;

    public FenetrePrincipaleClient(
        IPasserelleGoogleSheets passerelleGoogleSheets,
        IServiceCapturePaquetsReseau serviceCaptureReseau,
        IOrchestrateurSynchronisationMarche orchestrateurSynchronisation,
        AssistantGuidageCraftCape assistantGuidage,
        JournaliseurConsoleEtFichier journaliseur,
        string repertoireTravail)
    {
        InitializeComponent();

        _passerelleGoogleSheets = passerelleGoogleSheets;
        _serviceCaptureReseau = serviceCaptureReseau;
        _orchestrateurSynchronisation = orchestrateurSynchronisation;
        _assistantGuidage = assistantGuidage;
        _journaliseur = journaliseur;
        _repertoireTravail = repertoireTravail;

        ChampUrlOuIdSheet.Text = _passerelleGoogleSheets.IdentifiantClasseurActif;

        InitialiserListeCapes();

        _journaliseur.SurNouvelleEntreeJournal += SurEntreeJournalRecue;
        _assistantGuidage.SurMiseAJourGuidage += () => Dispatcher.InvokeAsync(ActualiserAffichageGuidage);

        _minuteurRafraichissementUi = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _minuteurRafraichissementUi.Tick += (_, _) => MettreAJourBandeauStatutReseau();

        Loaded += SurChargementFenetreAsync;
        Closed += SurFermetureFenetreAsync;
    }

    private void InitialiserListeCapes()
    {
        bool premiere = true;
        foreach (var recette in PlanificateurRequetesCraftCape.CatalogueCapesDisponibles)
        {
            _elementsCapes.Add(new ElementSelectionCape(recette, estSelectionneeParDefaut: premiere));
            premiere = false;
        }

        ListeCasesCapes.ItemsSource = _elementsCapes;
    }

    /// <summary>
    /// Initialise la connexion au Bot Google Sheets, démarre l'écoute réseau Npcap sur les cartes actives
    /// et charge la grille 'Indice prix' au lancement de la fenêtre.
    /// </summary>
    private async void SurChargementFenetreAsync(object sender, RoutedEventArgs e)
    {
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        _initialisationTerminee = true;
        AjouterLigneJournal("Initialisation du client Albion Market Sync v2.0...");

        await _orchestrateurSynchronisation.InitialiserAsync(_repertoireTravail);
        TexteModeAuthentificationGoogle.Text = _passerelleGoogleSheets.DescriptionModeAuthentification;
        BoutonConnecterGoogle.Content = "🤖 Copier l'e-mail du Bot Google";

        if (ServiceCaptureReseauNpcap.VerifierInstallationPiloteNpcap(out string messageNpcap))
        {
            int nombreCartes = _orchestrateurSynchronisation.DemarrerCaptureEnDirect();
            AjouterLigneJournal($"🟢 {messageNpcap} — Écoute active sur {nombreCartes} interface(s).");
        }
        else
        {
            AjouterLigneJournal($"🔴 {messageNpcap}");
        }

        await _assistantGuidage.ActualiserDepuisFeuilleIndicePrixAsync();
        ActualiserAffichageGuidage();
        MettreAJourBandeauStatutReseau();
        _minuteurRafraichissementUi.Start();
    }

    private async void SurFermetureFenetreAsync(object? sender, EventArgs e)
    {
        _minuteurRafraichissementUi.Stop();
        await _orchestrateurSynchronisation.ArreterEtViderFileAsync();
    }

    /// <summary>
    /// Met à jour chaque seconde le voyant d'état Npcap (🟢/🟡/🔴), la ville détectée et les compteurs de paquets/cellules.
    /// </summary>
    private void MettreAJourBandeauStatutReseau()
    {
        var etat = _orchestrateurSynchronisation.EtatSanteCapture;
        switch (etat)
        {
            case EtatSanteCaptureReseau.CaptureTraficAlbionActive:
                PastilleStatutReseau.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                TexteStatutReseau.Text = $"🟢 Albion connecté ({_serviceCaptureReseau.NomCarteReseauActive ?? "Réseau actif"}) — Ville : {_orchestrateurSynchronisation.VilleActive}";
                break;

            case EtatSanteCaptureReseau.EnEcouteAttenteJeu:
                PastilleStatutReseau.Fill = new SolidColorBrush(Color.FromRgb(234, 179, 8));
                TexteStatutReseau.Text = "🟡 Npcap en écoute — Ouvrez l'Hôtel des Ventes dans Albion Online";
                break;

            default:
                PastilleStatutReseau.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                TexteStatutReseau.Text = "🔴 Pilote Npcap inactif ou droits administrateur requis";
                break;
        }

        TexteCompteursCapture.Text =
            $"Paquets UDP : {_orchestrateurSynchronisation.NombreTotalPaquetsUdp:N0}  |  " +
            $"Pages HV : {_orchestrateurSynchronisation.NombreTotalPagesMarcheDecodees:N0}  |  " +
            $"Cellules sync. : {_orchestrateurSynchronisation.NombreTotalCellulesSynchronisees:N0}";
    }

    /// <summary>
    /// Rafraîchit le tableau de progression et l'encadré vert indiquant la prochaine recherche exacte à faire dans l'Hôtel des Ventes.
    /// </summary>
    private void ActualiserAffichageGuidage()
    {
        var plan = _assistantGuidage.PlanRequetesActuel;
        var prochaine = _assistantGuidage.ProchaineRequeteAFaire;
        int validees = _assistantGuidage.NombreEtapesValidees;
        int total = _assistantGuidage.NombreEtapesTotales;

        TableauPlanRequetes.ItemsSource = plan;
        TexteProgressionEtapes.Text = $"{validees} / {total} ingrédients validés dans 'Indice prix'";
        BarreProgressionRequetes.Maximum = Math.Max(1, total);
        BarreProgressionRequetes.Value = validees;

        if (prochaine is not null)
        {
            TexteCategorieProchaineRequete.Text = $"  —  Étape #{prochaine.NumeroOrdre} ({prochaine.CategorieIngredient})";
            TexteNomObjetProchaineRequete.Text = prochaine.NomObjetAChercherEnJeu;
            TexteTierProchaineRequete.Text = prochaine.FiltrePalierTier;
            TexteEnchantementProchaineRequete.Text = prochaine.FiltreEnchantement;
            string infoActuelle = prochaine.PrixActuelDansIndicePrix > 0
                ? $"Actuel : {prochaine.PrixActuelDansIndicePrix:N0} Ag"
                : "Manquant (0 Ag)";
            TexteCelluleCibleProchaineRequete.Text = $"Cellule cible : {prochaine.CoordonneeCelluleIndicePrix} ({infoActuelle})";
            TableauPlanRequetes.SelectedItem = prochaine;
            TableauPlanRequetes.ScrollIntoView(prochaine);
        }
        else if (total > 0)
        {
            TexteCategorieProchaineRequete.Text = "  —  TERMINÉ !";
            TexteNomObjetProchaineRequete.Text = "✅ Tous les prix sont dans 'Indice prix' !";
            TexteTierProchaineRequete.Text = "OK";
            TexteEnchantementProchaineRequete.Text = "OK";
            TexteCelluleCibleProchaineRequete.Text = "L'onglet 'Cout de craft' est 100 % complet pour votre sélection.";
        }
        else
        {
            TexteCategorieProchaineRequete.Text = "  —  Aucune cape cochée";
            TexteNomObjetProchaineRequete.Text = "Cochez au moins une cape à gauche";
            TexteTierProchaineRequete.Text = "-";
            TexteEnchantementProchaineRequete.Text = "-";
            TexteCelluleCibleProchaineRequete.Text = string.Empty;
        }
    }

    private void AppliquerFiltresUtilisateurVersAssistant()
    {
        if (!_initialisationTerminee)
            return;

        _assistantGuidage.CapesSelectionnees.Clear();
        foreach (var item in _elementsCapes.Where(c => c.EstSelectionnee))
        {
            _assistantGuidage.CapesSelectionnees.Add(item.Recette);
        }

        if (ListeTierMax.SelectedItem is ComboBoxItem itemTier &&
            int.TryParse(Convert.ToString(itemTier.Tag), out int tierMax))
        {
            _assistantGuidage.PalierTierMaximum = tierMax;
        }

        if (ListeEnchantementMax.SelectedItem is ComboBoxItem itemEnchant &&
            int.TryParse(Convert.ToString(itemEnchant.Tag), out int enchantMax))
        {
            _assistantGuidage.EnchantementMaximum = enchantMax;
        }

        _assistantGuidage.ExigerActualisationSession = CaseExigerActualisationSession.IsChecked == true;
        _assistantGuidage.RecalculerPlanEtProchaineRequete();
    }

    private void SurModificationFiltresCraft(object sender, RoutedEventArgs e)
        => AppliquerFiltresUtilisateurVersAssistant();

    private void SurModificationCaseCape(object sender, RoutedEventArgs e)
        => AppliquerFiltresUtilisateurVersAssistant();

    private void SurClicToutCocherCapes(object sender, RoutedEventArgs e)
    {
        foreach (var cape in _elementsCapes)
            cape.EstSelectionnee = true;
        AppliquerFiltresUtilisateurVersAssistant();
    }

    private void SurClicToutDecocherCapes(object sender, RoutedEventArgs e)
    {
        foreach (var cape in _elementsCapes)
            cape.EstSelectionnee = false;
        AppliquerFiltresUtilisateurVersAssistant();
    }

    private void SurClicCopierNomRecherche(object sender, RoutedEventArgs e)
    {
        var prochaine = _assistantGuidage.ProchaineRequeteAFaire;
        if (prochaine is not null)
        {
            Clipboard.SetText(prochaine.NomObjetAChercherEnJeu);
            AjouterLigneJournal($"📋 Copié dans le presse-papier : « {prochaine.NomObjetAChercherEnJeu} » (Tier {prochaine.FiltrePalierTier}, Enchant. {prochaine.FiltreEnchantement})");
        }
    }

    private void SurClicPasserEtape(object sender, RoutedEventArgs e)
    {
        _assistantGuidage.IgnorerProchaineEtape();
    }

    private void SurClicReinitialiserSession(object sender, RoutedEventArgs e)
    {
        _assistantGuidage.ReinitialiserProgressionSession();
    }

    private async void SurClicActualiserFeuilleIndicePrix(object sender, RoutedEventArgs e)
    {
        AjouterLigneJournal("🔄 Lecture de la feuille 'Indice prix' en cours...");
        await _assistantGuidage.ActualiserDepuisFeuilleIndicePrixAsync();
        AjouterLigneJournal("✅ Feuille 'Indice prix' relue et plan de requêtes mis à jour.");
    }

    private async void SurClicAppliquerSheet(object sender, RoutedEventArgs e)
    {
        string nouveauSheetId = PasserelleApiGoogleSheets.ExtraireIdentifiantClasseurDepuisUrlOuId(ChampUrlOuIdSheet.Text);
        if (string.IsNullOrWhiteSpace(nouveauSheetId))
            return;

        _passerelleGoogleSheets.IdentifiantClasseurActif = nouveauSheetId;
        ChampUrlOuIdSheet.Text = nouveauSheetId;
        AjouterLigneJournal($"📄 Classeur Google Sheet actif défini sur : {nouveauSheetId}");
        await _assistantGuidage.ActualiserDepuisFeuilleIndicePrixAsync();
    }

    private async void SurClicConnecterCompteGoogle(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(PasserelleApiGoogleSheets.EmailBotGoogleIntegre);
        AjouterLigneJournal($"📋 E-mail du Bot Google copié : {PasserelleApiGoogleSheets.EmailBotGoogleIntegre}");

        MessageBox.Show(
            this,
            $"L'adresse e-mail du Bot Google a été copiée dans votre presse-papier :\n\n" +
            $"{PasserelleApiGoogleSheets.EmailBotGoogleIntegre}\n\n" +
            $"Pour autoriser le Bot à remplir votre Google Sheet :\n" +
            $"1. Ouvrez votre Google Sheet dans votre navigateur.\n" +
            $"2. Cliquez sur le bouton « Partager » (en haut à droite).\n" +
            $"3. Collez cette adresse e-mail (Ctrl+V) en mode « Éditeur » et validez !",
            "🤖 E-mail du Bot Google copié",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        await _assistantGuidage.ActualiserDepuisFeuilleIndicePrixAsync();
    }

    private void SurChangementStrategiePrix(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialisationTerminee)
            return;

        _orchestrateurSynchronisation.StrategiePrixActive = ListeStrategiePrix.SelectedIndex == 1
            ? StrategieCalculPrix.MoyennePonderee
            : StrategieCalculPrix.PrixVenteMinimum;

        AjouterLigneJournal($"⚙️ Stratégie de prix changée : {_orchestrateurSynchronisation.StrategiePrixActive}");
    }

    private void SurEntreeJournalRecue(string message, NiveauSeveriteJournal severite)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        Dispatcher.InvokeAsync(() => AjouterLigneJournal(message.Trim()));
    }

    private void AjouterLigneJournal(string texte)
    {
        string ligneHorodatee = $"[{DateTime.Now:HH:mm:ss}] {texte}";
        ListeJournalTempsReel.Items.Add(ligneHorodatee);
        if (ListeJournalTempsReel.Items.Count > 250)
            ListeJournalTempsReel.Items.RemoveAt(0);

        ListeJournalTempsReel.ScrollIntoView(ligneHorodatee);
    }
}
