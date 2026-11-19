using System.Globalization;
using System.Text;
using System.Text.Json;
using AlbionMarketSync.Application.Configuration;
using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Interfaces;
using AlbionMarketSync.Domain.ServicesMetier;
using AlbionMarketSync.Infrastructure.Serialisation;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Util.Store;

namespace AlbionMarketSync.Infrastructure.GoogleSheets;

/// <summary>
/// Passerelle technique de communication avec Google Sheets API v4 utilisant l'API officielle
/// d'authentification Google OAuth 2.0 (GoogleWebAuthorizationBroker).
/// </summary>
public sealed class PasserelleApiGoogleSheets(ConfigurationApplication configurationApplication) : IPasserelleGoogleSheets
{
    public const string EmailBotGoogleIntegre = "albion-bot@albionmarketsync-515108.iam.gserviceaccount.com";

    private SheetsService? _serviceGoogleSheets;
    private string _repertoireTravail = Directory.GetCurrentDirectory();

    public string IdentifiantClasseurActif { get; set; } = configurationApplication.IdentifiantClasseurGoogleSheet;
    public string IdentifiantClientOAuth { get; private set; } = configurationApplication.AuthentificationGoogleOAuth.IdentifiantClientOAuth;
    public string SecretClientOAuth { get; private set; } = configurationApplication.AuthentificationGoogleOAuth.SecretClientOAuth;
    public string DescriptionModeAuthentification { get; private set; } = "Non initialisé";
    public bool EstConnecteEnDirectApiGoogle => _serviceGoogleSheets is not null;

    public async Task InitialiserConnexionAsync(string repertoireTravail, CancellationToken jetonAnnulation = default)
    {
        _repertoireTravail = repertoireTravail;
        IdentifiantClasseurActif = ExtraireIdentifiantClasseurDepuisUrlOuId(IdentifiantClasseurActif);

        string dossierConfiguration = Path.Combine(repertoireTravail, "Infrastructure", "Configuration");
        Directory.CreateDirectory(dossierConfiguration);

        // 1. Connexion prioritaire et automatique via le Bot Google (service-account.json)
        string cheminCleCompteService = Path.IsPathRooted(configurationApplication.CheminCleCompteService)
            ? configurationApplication.CheminCleCompteService
            : Path.Combine(repertoireTravail, configurationApplication.CheminCleCompteService);

        if (File.Exists(cheminCleCompteService))
        {
            if (await ConnecterCompteGoogleUtilisateurAsync(cheminCleCompteService, jetonAnnulation))
                return;
        }

        // 2. Repli éventuel sur client_secrets.json si un jeton OAuth existe déjà
        string cheminSecretsClient = Path.Combine(dossierConfiguration, "client_secrets.json");
        string dossierJetonsOAuth = Path.Combine(dossierConfiguration, "JetonsGoogleOAuth");
        if (File.Exists(cheminSecretsClient) && Directory.Exists(dossierJetonsOAuth) && Directory.GetFiles(dossierJetonsOAuth).Length > 0)
        {
            if (await ConnecterCompteGoogleUtilisateurAsync(cheminSecretsClient, jetonAnnulation))
                return;
        }

        DescriptionModeAuthentification = $"🟡 Clé service-account.json absente dans Infrastructure/Configuration/ ({EmailBotGoogleIntegre})";
    }

    /// <summary>
    /// Authentifie directement le joueur via l'API officielle Google OAuth 2.0 (GoogleWebAuthorizationBroker).
    /// Ouvre automatiquement la page de connexion Google dans le navigateur par défaut et stocke le jeton OAuth2.
    /// </summary>
    public async Task<bool> ConnecterAvecApiGoogleOAuthAsync(
        string identifiantClientOAuth,
        string secretClientOAuth,
        CancellationToken jetonAnnulation = default)
    {
        if (string.IsNullOrWhiteSpace(identifiantClientOAuth) || string.IsNullOrWhiteSpace(secretClientOAuth))
            return false;

        try
        {
            string dossierConfiguration = Path.Combine(_repertoireTravail, "Infrastructure", "Configuration");
            string dossierJetonsOAuth = Path.Combine(dossierConfiguration, "JetonsGoogleOAuth");
            Directory.CreateDirectory(dossierJetonsOAuth);

            var secretsClient = new ClientSecrets
            {
                ClientId = identifiantClientOAuth.Trim(),
                ClientSecret = secretClientOAuth.Trim()
            };

            var identifiantsUtilisateur = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secretsClient,
                [SheetsService.Scope.Spreadsheets],
                "joueur_albion",
                jetonAnnulation,
                new FileDataStore(dossierJetonsOAuth, true));

            _serviceGoogleSheets?.Dispose();
            _serviceGoogleSheets = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = identifiantsUtilisateur,
                ApplicationName = "AlbionMarketSync/2.0"
            });

            IdentifiantClientOAuth = secretsClient.ClientId;
            SecretClientOAuth = secretsClient.ClientSecret;

            await SauvegarderIdentifiantsOAuthDansConfigAsync(IdentifiantClientOAuth, SecretClientOAuth, jetonAnnulation);

            DescriptionModeAuthentification = "🟢 Connecté via l'API d'authentification Google OAuth 2.0 (Envoi direct actif)";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SauvegarderIdentifiantsOAuthDansConfigAsync(
        string identifiantClient,
        string secretClient,
        CancellationToken jetonAnnulation)
    {
        try
        {
            string cheminConfig = Path.Combine(_repertoireTravail, "Infrastructure", "Configuration", "config.json");
            var nouvelleConfig = configurationApplication with
            {
                IdentifiantClasseurGoogleSheet = IdentifiantClasseurActif,
                AuthentificationGoogleOAuth = new ConfigurationGoogleOAuth
                {
                    IdentifiantClientOAuth = identifiantClient,
                    SecretClientOAuth = secretClient
                }
            };

            string json = JsonSerializer.Serialize(nouvelleConfig, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cheminConfig, json, new UTF8Encoding(false), jetonAnnulation);
        }
        catch
        {
        }
    }

    public async Task<bool> ConnecterCompteGoogleUtilisateurAsync(
        string? cheminFichierSecretOuCle = null,
        CancellationToken jetonAnnulation = default)
    {
        try
        {
            string dossierConfiguration = Path.Combine(_repertoireTravail, "Infrastructure", "Configuration");
            Directory.CreateDirectory(dossierConfiguration);

            string cheminEffectif = !string.IsNullOrWhiteSpace(cheminFichierSecretOuCle)
                ? cheminFichierSecretOuCle
                : Path.Combine(dossierConfiguration, "client_secrets.json");

            if (!File.Exists(cheminEffectif))
                return false;

            string contenuJson = await File.ReadAllTextAsync(cheminEffectif, Encoding.UTF8, jetonAnnulation);

            if (contenuJson.Contains("\"installed\"", StringComparison.OrdinalIgnoreCase) ||
                contenuJson.Contains("\"web\"", StringComparison.OrdinalIgnoreCase))
            {
                await using var fluxSecrets = new FileStream(cheminEffectif, FileMode.Open, FileAccess.Read);
                var secretsClient = await GoogleClientSecrets.FromStreamAsync(fluxSecrets, jetonAnnulation);

                return await ConnecterAvecApiGoogleOAuthAsync(
                    secretsClient.Secrets.ClientId,
                    secretsClient.Secrets.ClientSecret,
                    jetonAnnulation);
            }

            if (contenuJson.Contains("\"service_account\"", StringComparison.OrdinalIgnoreCase))
            {
                GoogleCredential identifiantsService;
                await using (var fluxCle = new FileStream(cheminEffectif, FileMode.Open, FileAccess.Read))
                {
                    identifiantsService = GoogleCredential.FromStream(fluxCle)
                        .CreateScoped(SheetsService.Scope.Spreadsheets);
                }

                _serviceGoogleSheets?.Dispose();
                _serviceGoogleSheets = new SheetsService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = identifiantsService,
                    ApplicationName = "AlbionMarketSync/2.0"
                });

                DescriptionModeAuthentification = $"🟢 Bot Google actif — Partagez votre Google Sheet (Éditeur) avec : {EmailBotGoogleIntegre}";
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extrait l'identifiant du Google Sheet à partir d'une URL complète (https://docs.google.com/spreadsheets/d/ID/edit)
    /// ou retourne directement la chaîne s'il s'agit déjà d'un identifiant brut.
    /// </summary>
    public static string ExtraireIdentifiantClasseurDepuisUrlOuId(string urlOuId)
    {
        if (string.IsNullOrWhiteSpace(urlOuId))
            return string.Empty;

        string texte = urlOuId.Trim();
        const string marqueurDebut = "/spreadsheets/d/";
        int indexDebut = texte.IndexOf(marqueurDebut, StringComparison.OrdinalIgnoreCase);
        if (indexDebut >= 0)
        {
            string apresMarqueur = texte[(indexDebut + marqueurDebut.Length)..];
            int indexSlash = apresMarqueur.IndexOf('/');
            return indexSlash > 0 ? apresMarqueur[..indexSlash] : apresMarqueur;
        }

        return texte;
    }

    public async Task<IReadOnlyList<IReadOnlyList<string>>> LireGrilleOngletSelectionAsync(CancellationToken jetonAnnulation = default)
    {
        string repertoireLogs = Path.Combine(_repertoireTravail, "Infrastructure", "Logs");
        Directory.CreateDirectory(repertoireLogs);
        string cheminCaptureSelection = Path.Combine(repertoireLogs, "selection_snapshot.json");

        if (_serviceGoogleSheets is not null)
        {
            var requeteLecture = _serviceGoogleSheets.Spreadsheets.Values.Get(
                IdentifiantClasseurActif,
                "'Sélection'!A1:AA55");

            var reponseLecture = await requeteLecture.ExecuteAsync(jetonAnnulation);
            if (reponseLecture.Values is not null)
            {
                var grilleValeurs = reponseLecture.Values
                    .Select(ligne => ligne.Select(cellule => Convert.ToString(cellule, CultureInfo.InvariantCulture) ?? string.Empty).ToArray())
                    .ToArray();

                string texteJsonGrille = JsonSerializer.Serialize(grilleValeurs, ContexteSerialisationJsonAlbion.Default.StringArrayArray);
                await File.WriteAllTextAsync(cheminCaptureSelection, texteJsonGrille, new UTF8Encoding(false), jetonAnnulation);
                return grilleValeurs;
            }
        }

        return await LireGrilleLocaleJsonAsync(cheminCaptureSelection, jetonAnnulation);
    }

    public async Task<IReadOnlyList<IReadOnlyList<string>>> LireGrilleIndicePrixAsync(CancellationToken jetonAnnulation = default)
    {
        string repertoireLogs = Path.Combine(_repertoireTravail, "Infrastructure", "Logs");
        Directory.CreateDirectory(repertoireLogs);
        string cheminCaptureIndicePrix = Path.Combine(repertoireLogs, "indice_prix_snapshot.json");

        if (_serviceGoogleSheets is not null)
        {
            try
            {
                var requeteLecture = _serviceGoogleSheets.Spreadsheets.Values.Get(
                    IdentifiantClasseurActif,
                    "'Indice prix'!A1:V42");

                var reponseLecture = await requeteLecture.ExecuteAsync(jetonAnnulation);
                if (reponseLecture.Values is not null)
                {
                    var grilleValeurs = reponseLecture.Values
                        .Select(ligne => ligne.Select(cellule => Convert.ToString(cellule, CultureInfo.InvariantCulture) ?? string.Empty).ToArray())
                        .ToArray();

                    string texteJsonGrille = JsonSerializer.Serialize(grilleValeurs, ContexteSerialisationJsonAlbion.Default.StringArrayArray);
                    await File.WriteAllTextAsync(cheminCaptureIndicePrix, texteJsonGrille, new UTF8Encoding(false), jetonAnnulation);
                    return grilleValeurs;
                }
            }
            catch
            {
                // Repli sur l'instantané local ci-dessous
            }
        }

        return await LireGrilleLocaleJsonAsync(cheminCaptureIndicePrix, jetonAnnulation);
    }

    /// <summary>
    /// Envoie un lot de cellules modifiées vers Google Sheets en un seul appel HTTP batchUpdate,
    /// et met à jour l'instantané JSON local (indice_prix_snapshot.json) pour un affichage instantané dans l'UI.
    /// </summary>
    public async Task EnvoyerLotMisesAJourCellulesAsync(IReadOnlyList<MiseAJourCelluleSheet> lotCellules, CancellationToken jetonAnnulation = default)
    {
        await EnregistrerFichierLotEnAttenteAsync(lotCellules, jetonAnnulation);
        await MettreAJourGrilleIndicePrixLocaleAsync(lotCellules, jetonAnnulation);

        if (_serviceGoogleSheets is not null)
        {
            var plagesValeurs = lotCellules.Select(miseAJour => new ValueRange
            {
                Range = miseAJour.CoordonneeCellule,
                Values = new List<IList<object>> { new List<object> { Math.Round(miseAJour.PrixArgentArrondi) } }
            }).ToList();

            var corpsRequeteBatch = new BatchUpdateValuesRequest
            {
                ValueInputOption = "USER_ENTERED",
                Data = plagesValeurs
            };

            var requeteBatch = _serviceGoogleSheets.Spreadsheets.Values.BatchUpdate(
                corpsRequeteBatch,
                IdentifiantClasseurActif);

            await requeteBatch.ExecuteAsync(jetonAnnulation);
        }
    }

    private async Task MettreAJourGrilleIndicePrixLocaleAsync(
        IReadOnlyList<MiseAJourCelluleSheet> lotCellules,
        CancellationToken jetonAnnulation)
    {
        string repertoireLogs = Path.Combine(_repertoireTravail, "Infrastructure", "Logs");
        Directory.CreateDirectory(repertoireLogs);
        string cheminCaptureIndicePrix = Path.Combine(repertoireLogs, "indice_prix_snapshot.json");

        var grilleExistante = await LireGrilleLocaleJsonAsync(cheminCaptureIndicePrix, jetonAnnulation);
        var listeLignesModifiables = grilleExistante
            .Select(ligne => ligne.ToList())
            .ToList();

        while (listeLignesModifiables.Count < 42)
            listeLignesModifiables.Add(new List<string>());

        foreach (var miseAJour in lotCellules)
        {
            if (!miseAJour.CoordonneeCellule.StartsWith("'Indice prix'!", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!PlanificateurRequetesCraftCape.EssayerConvertirCoordonneeEnIndicesZero(
                    miseAJour.CoordonneeCellule,
                    out int indexLigne0,
                    out int indexColonne0))
            {
                continue;
            }

            while (listeLignesModifiables.Count <= indexLigne0)
                listeLignesModifiables.Add(new List<string>());

            var ligneCible = listeLignesModifiables[indexLigne0];
            while (ligneCible.Count <= indexColonne0)
                ligneCible.Add(string.Empty);

            ligneCible[indexColonne0] = ((long)Math.Round(miseAJour.PrixArgentArrondi)).ToString(CultureInfo.InvariantCulture);
        }

        string[][] tableauFinal = listeLignesModifiables.Select(l => l.ToArray()).ToArray();
        string jsonMisAJour = JsonSerializer.Serialize(tableauFinal, ContexteSerialisationJsonAlbion.Default.StringArrayArray);
        await File.WriteAllTextAsync(cheminCaptureIndicePrix, jsonMisAJour, new UTF8Encoding(false), jetonAnnulation);
    }

    private static async Task<IReadOnlyList<IReadOnlyList<string>>> LireGrilleLocaleJsonAsync(
        string cheminFichierJson,
        CancellationToken jetonAnnulation)
    {
        if (!File.Exists(cheminFichierJson))
            return Array.Empty<string[]>();

        try
        {
            using var lecteurFichier = new StreamReader(cheminFichierJson, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string contenuBrut = await lecteurFichier.ReadToEndAsync(jetonAnnulation);
            int indexPremierCrochet = contenuBrut.IndexOf('[');
            if (indexPremierCrochet >= 0)
                contenuBrut = contenuBrut[indexPremierCrochet..];

            var grilleDeserialisee = JsonSerializer.Deserialize(contenuBrut, ContexteSerialisationJsonAlbion.Default.StringArrayArray);
            return grilleDeserialisee ?? Array.Empty<string[]>();
        }
        catch
        {
            return Array.Empty<string[]>();
        }
    }

    private async Task EnregistrerFichierLotEnAttenteAsync(IReadOnlyList<MiseAJourCelluleSheet> lotCellules, CancellationToken jetonAnnulation)
    {
        string repertoireLogs = Path.Combine(_repertoireTravail, "Infrastructure", "Logs");
        Directory.CreateDirectory(repertoireLogs);
        string cheminFichierLot = Path.Combine(repertoireLogs, "pending_sheets_batch.json");

        var operationsDuLot = lotCellules.Select(miseAJour => new OperationEcritureLotGoogleSheet(
            TypeOperation: "write",
            PlageCellule: miseAJour.CoordonneeCellule,
            ValeursCellule: [[(long)Math.Round(miseAJour.PrixArgentArrondi)]]
        )).ToList();

        string contenuJsonFinal = JsonSerializer.Serialize(
            operationsDuLot,
            ContexteSerialisationJsonAlbion.Default.ListOperationEcritureLotGoogleSheet);

        await File.WriteAllTextAsync(cheminFichierLot, contenuJsonFinal, new UTF8Encoding(false), jetonAnnulation);
    }

    public void Dispose()
    {
        _serviceGoogleSheets?.Dispose();
    }
}
