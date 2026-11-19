using System.IO;
using System.Text;
using System.Text.Json;
using AlbionMarketSync.Application.CasUtilisation;
using AlbionMarketSync.Application.Configuration;
using AlbionMarketSync.Domain.ServicesMetier;
using AlbionMarketSync.Infrastructure.GoogleSheets;
using AlbionMarketSync.Infrastructure.Journalisation;
using AlbionMarketSync.Infrastructure.Persistance;
using AlbionMarketSync.Infrastructure.Photon;
using AlbionMarketSync.Infrastructure.Reseau;
using AlbionMarketSync.Infrastructure.Serialisation;
using AlbionMarketSync.Presentation;

namespace AlbionMarketSync;

/// <summary>
/// Point d'entrée principal de l'application AlbionMarketSync.
/// Assemble les 4 couches de l'architecture DAIP (Domain, Application, Infrastructure, Presentation)
/// et démarre soit l'interface graphique WPF (par défaut), soit le contrôleur CLI si des arguments sont passés.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string repertoireExecutable = AppContext.BaseDirectory;
        string repertoireTravail = Directory.GetCurrentDirectory();
        string repertoireLogs = Path.Combine(repertoireTravail, "Infrastructure", "Logs");
        Directory.CreateDirectory(repertoireLogs);

        // Recherche un fichier de configuration d'abord dans le dossier de travail, puis dans Infrastructure/Configuration
        string ResoudreCheminFichierConfiguration(string nomFichier)
        {
            string cheminDansRepertoireTravail = Path.Combine(repertoireTravail, nomFichier);
            if (File.Exists(cheminDansRepertoireTravail))
                return cheminDansRepertoireTravail;

            string cheminDansInfrastructureConfig = Path.Combine(repertoireTravail, "Infrastructure", "Configuration", Path.GetFileName(nomFichier));
            if (File.Exists(cheminDansInfrastructureConfig))
                return cheminDansInfrastructureConfig;

            return Path.Combine(repertoireExecutable, nomFichier);
        }

        // 1. Chargement des fichiers JSON de configuration (config.json, opcodes.json, items.json)
        string cheminConfiguration = ResoudreCheminFichierConfiguration(Path.Combine("Infrastructure", "Configuration", "config.json"));
        var configurationApplication = JsonSerializer.Deserialize(
            File.ReadAllText(cheminConfiguration),
            ContexteSerialisationJsonAlbion.Default.ConfigurationApplication) ?? new ConfigurationApplication();

        string cheminCodesOperations = ResoudreCheminFichierConfiguration(configurationApplication.CheminFichierCodesOperations);
        var configurationCodesOperations = JsonSerializer.Deserialize(
            File.ReadAllText(cheminCodesOperations),
            ContexteSerialisationJsonAlbion.Default.ConfigurationCodesOperations) ?? new ConfigurationCodesOperations();

        var dictionnaireNomsObjets = JsonSerializer.Deserialize(
            File.ReadAllText(ResoudreCheminFichierConfiguration(configurationApplication.CheminFichierNomsObjets)),
            ContexteSerialisationJsonAlbion.Default.DictionaryStringString) ?? new Dictionary<string, string>();

        string cheminBaseSqlite = Path.IsPathRooted(configurationApplication.CheminBaseCacheSqlite)
            ? configurationApplication.CheminBaseCacheSqlite
            : Path.Combine(repertoireTravail, configurationApplication.CheminBaseCacheSqlite);

        // 2. Instanciation et injection des dépendances (Domain -> Application -> Infrastructure -> Presentation)
        using var journaliseur = new JournaliseurConsoleEtFichier(Path.Combine(repertoireLogs, "live_capture.log"));
        using var cachePrixLocal = new CachePrixMarcheSqlite(cheminBaseSqlite);
        using var passerelleGoogleSheets = new PasserelleApiGoogleSheets(configurationApplication);
        using var serviceCaptureReseau = new ServiceCaptureReseauNpcap(configurationApplication.CaptureReseau);

        var moteurCartographie = new MoteurCartographieCellules(dictionnaireNomsObjets);
        var analyseurPaquetsPhoton = new AnalyseurPaquetsUdpPhoton();
        var deserialiseurOrdres = new DeserialiseurOrdresMarcheJson();

        var processeurMarche = new ProcesseurPaquetsMarcheAlbion(
            configurationApplication,
            configurationCodesOperations,
            moteurCartographie,
            deserialiseurOrdres);

        using var coordinateurSynchronisation = new CoordinateurSynchronisationGoogleSheets(
            configurationApplication,
            moteurCartographie,
            cachePrixLocal,
            passerelleGoogleSheets,
            journaliseur);

        using var orchestrateurSynchronisation = new OrchestrateurSynchronisationMarche(
            serviceCaptureReseau,
            analyseurPaquetsPhoton,
            processeurMarche,
            coordinateurSynchronisation);

        var assistantGuidage = new AssistantGuidageCraftCape(passerelleGoogleSheets, cachePrixLocal, coordinateurSynchronisation);

        // 3. Mode ligne de commande (si des arguments comme --console, --probe ou --replay-captured sont fournis)
        if (args.Length > 0)
        {
            using var sourceAnnulation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, evenement) =>
            {
                evenement.Cancel = true;
                sourceAnnulation.Cancel();
            };

            var controleurCli = new ControleurLigneDeCommande(
                configurationApplication,
                configurationCodesOperations,
                cheminCodesOperations,
                repertoireTravail,
                moteurCartographie,
                cachePrixLocal,
                serviceCaptureReseau,
                analyseurPaquetsPhoton,
                processeurMarche,
                coordinateurSynchronisation,
                orchestrateurSynchronisation,
                assistantGuidage,
                journaliseur);

            return controleurCli.ExecuterAsync(args, sourceAnnulation.Token).GetAwaiter().GetResult();
        }

        // 4. Mode par défaut : lancement de l'interface graphique WPF
        var applicationWpf = new System.Windows.Application();
        var fenetrePrincipale = new FenetrePrincipaleClient(
            passerelleGoogleSheets,
            serviceCaptureReseau,
            orchestrateurSynchronisation,
            assistantGuidage,
            journaliseur,
            repertoireTravail);

        return applicationWpf.Run(fenetrePrincipale);
    }
}
