using System.Text.Json.Serialization;
using AlbionMarketSync.Domain.Enumerations;

namespace AlbionMarketSync.Application.Configuration;

/// <summary>
/// Configuration globale de l'application mappée sur le fichier Infrastructure/Configuration/config.json.
/// </summary>
public sealed record ConfigurationApplication
{
    /// <summary>Identifiant du classeur Google Sheet actif (extrait de l'URL /spreadsheets/d/...).</summary>
    [JsonPropertyName("SpreadsheetId")]
    public string IdentifiantClasseurGoogleSheet { get; init; } = "1SRcjuuz8vVaPb9v4ow1COQyERDUS8_h2w9zYzQZidew";

    /// <summary>Chemin de la clé JSON du compte de service (Bot Google intégré).</summary>
    [JsonPropertyName("ServiceAccountKeyPath")]
    public string CheminCleCompteService { get; init; } = "Infrastructure/Configuration/service-account.json";

    [JsonPropertyName("CellMappingPath")]
    public string CheminFichierCartographieCellules { get; init; } = "Infrastructure/Configuration/cell_mapping.json";

    [JsonPropertyName("OpcodesPath")]
    public string CheminFichierCodesOperations { get; init; } = "Infrastructure/Configuration/opcodes.json";

    [JsonPropertyName("ItemsPath")]
    public string CheminFichierNomsObjets { get; init; } = "Infrastructure/Configuration/items.json";

    [JsonPropertyName("SqliteCachePath")]
    public string CheminBaseCacheSqlite { get; init; } = "Infrastructure/Logs/market_cache.db";

    [JsonPropertyName("GoogleOAuth")]
    public ConfigurationGoogleOAuth AuthentificationGoogleOAuth { get; init; } = new();

    [JsonPropertyName("Capture")]
    public ConfigurationCaptureReseau CaptureReseau { get; init; } = new();

    [JsonPropertyName("Pricing")]
    public ConfigurationCalculPrix CalculPrix { get; init; } = new();

    [JsonPropertyName("Sync")]
    public ConfigurationSynchronisation Synchronisation { get; init; } = new();
}

/// <summary>
/// Paramètres du filtre réseau Npcap ciblant les ports UDP des serveurs d'Albion Online (5055, 5056, 4535).
/// </summary>
public sealed record ConfigurationCaptureReseau
{
    [JsonPropertyName("BpfFilter")]
    public string FiltreCaptureBpf { get; init; } = "udp and (port 5056 or port 5055 or port 4535)";

    [JsonPropertyName("AlbionPorts")]
    public int[] PortsUdpAlbion { get; init; } = [5056, 5055, 4535];

    [JsonPropertyName("ReadTimeoutMs")]
    public int DelaiLectureMillisecondes { get; init; } = 50;
}

public sealed record ConfigurationCalculPrix
{
    [JsonPropertyName("Strategy")]
    public string NomStrategie { get; init; } = "MinSellOrder";

    [JsonPropertyName("WeightedAverageTopUnits")]
    public int NombreUnitesMoyennePonderee { get; init; } = 10;

    [JsonPropertyName("SilverDivisor")]
    public double DiviseurArgent { get; init; } = 10000.0;

    public StrategieCalculPrix ObtenirStrategieCalcul()
        => string.Equals(NomStrategie, "WeightedAverage", StringComparison.OrdinalIgnoreCase)
            ? StrategieCalculPrix.MoyennePonderee
            : StrategieCalculPrix.PrixVenteMinimum;
}

public sealed record ConfigurationSynchronisation
{
    [JsonPropertyName("DebounceWindowMs")]
    public int FenetreRegroupementMillisecondes { get; init; } = 2500;

    [JsonPropertyName("EnableGoogleSheetsUpload")]
    public bool ActiverEnvoiGoogleSheets { get; init; } = true;
}

public sealed record ConfigurationCodesOperations
{
    [JsonPropertyName("EnableSignatureAutoDiscovery")]
    public bool ActiverDecouverteAutomatiqueParSignature { get; init; } = true;

    [JsonPropertyName("OperationCodes")]
    public Dictionary<string, short> CodesOperations { get; init; } = new();

    [JsonPropertyName("ClusterCityMapping")]
    public Dictionary<string, string> CorrespondanceClusterVille { get; init; } = new();
}

public sealed record ConfigurationGoogleOAuth
{
    [JsonPropertyName("ClientId")]
    public string IdentifiantClientOAuth { get; init; } = string.Empty;

    [JsonPropertyName("ClientSecret")]
    public string SecretClientOAuth { get; init; } = string.Empty;
}

