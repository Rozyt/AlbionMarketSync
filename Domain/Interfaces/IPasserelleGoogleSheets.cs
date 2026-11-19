using AlbionMarketSync.Domain.Entites;

namespace AlbionMarketSync.Domain.Interfaces;

/// <summary>
/// Contrat de la passerelle de communication avec Google Sheets API v4 (lecture des grilles et écriture groupée par lots).
/// </summary>
public interface IPasserelleGoogleSheets : IDisposable
{
    string IdentifiantClasseurActif { get; set; }

    string IdentifiantClientOAuth { get; }

    string SecretClientOAuth { get; }

    string DescriptionModeAuthentification { get; }

    bool EstConnecteEnDirectApiGoogle { get; }

    /// <summary>Initialise la connexion Google Sheets (via le Bot Google intégré par défaut ou jeton OAuth existant).</summary>
    Task InitialiserConnexionAsync(string repertoireTravail, CancellationToken jetonAnnulation = default);

    /// <summary>Authentifie un utilisateur via le flux OAuth 2.0 dans son navigateur par défaut.</summary>
    Task<bool> ConnecterAvecApiGoogleOAuthAsync(
        string identifiantClientOAuth,
        string secretClientOAuth,
        CancellationToken jetonAnnulation = default);

    /// <summary>Connecte l'API Google Sheets à partir d'un fichier JSON de clé (service_account ou client_secrets).</summary>
    Task<bool> ConnecterCompteGoogleUtilisateurAsync(string? cheminFichierSecretOuCle = null, CancellationToken jetonAnnulation = default);

    /// <summary>Lit la plage A1:Z60 de l'onglet 'Sélection'.</summary>
    Task<IReadOnlyList<IReadOnlyList<string>>> LireGrilleOngletSelectionAsync(CancellationToken jetonAnnulation = default);

    /// <summary>Lit la plage A1:W45 de l'onglet 'Indice prix'.</summary>
    Task<IReadOnlyList<IReadOnlyList<string>>> LireGrilleIndicePrixAsync(CancellationToken jetonAnnulation = default);

    /// <summary>Envoie un lot de mises à jour de cellules en une seule requête HTTP batchUpdate.</summary>
    Task EnvoyerLotMisesAJourCellulesAsync(IReadOnlyList<MiseAJourCelluleSheet> lotCellules, CancellationToken jetonAnnulation = default);
}
