using AlbionMarketSync.Domain.Enumerations;

namespace AlbionMarketSync.Domain.Entites;

/// <summary>
/// Représente un message applicatif ExitGames Photon entièrement décodé avec son dictionnaire de paramètres.
/// </summary>
public sealed record MessagePhotonDecode(
    TypeMessagePhoton TypeMessage,
    byte CodeEnteteMessage,
    short? CodeRetourServeur,
    string? MessageDebogage,
    Dictionary<byte, object?> ParametresMessage,
    string VersionProtocoleUtilisee = "Protocol18"
);

