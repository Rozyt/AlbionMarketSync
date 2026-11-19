namespace AlbionMarketSync.Domain.Enumerations;

/// <summary>
/// Types de messages applicatifs du protocole réseau ExitGames Photon utilisé par Albion Online.
/// </summary>
public enum TypeMessagePhoton : byte
{
    Initialisation = 0,
    ReponseInitialisation = 1,
    /// <summary>Requête envoyée par le client du joueur vers le serveur (OperationRequest).</summary>
    RequeteOperation = 2,
    /// <summary>Réponse du serveur à une action du joueur, ex: résultats de recherche Hôtel des Ventes (OperationResponse).</summary>
    ReponseOperation = 3,
    /// <summary>Événement asynchrone envoyé par le serveur, ex: changement de carte/ville (EventData).</summary>
    EvenementDonnees = 4,
    RequeteOperationInterne = 6,
    ReponseOperationInterne = 7
}

