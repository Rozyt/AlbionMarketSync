using System.Text.Json.Serialization;

namespace AlbionMarketSync.Domain.Entites;

/// <summary>
/// Représente une offre de vente (Sell Order) ou d'achat (Buy Order) brute reçue depuis le serveur d'Albion Online.
/// Note : Albion transmet les prix en dix-millièmes d'argent (il faut diviser par 10 000 pour obtenir le prix en Argent).
/// </summary>
public sealed record OrdreMarcheAlbion
{
    [JsonPropertyName("Id")]
    public long IdentifiantOffre { get; init; }

    [JsonPropertyName("UnitPriceSilver")]
    public long PrixUnitaireArgentBrut { get; init; }

    [JsonPropertyName("TotalPriceSilver")]
    public long PrixTotalArgentBrut { get; init; }

    [JsonPropertyName("Amount")]
    public int QuantiteDisponible { get; init; }

    [JsonPropertyName("Tier")]
    public int PalierTier { get; init; }

    [JsonPropertyName("IsFinished")]
    public bool EstTerminee { get; init; }

    [JsonPropertyName("AuctionType")]
    public string TypeEnchere { get; init; } = string.Empty;

    [JsonPropertyName("ItemTypeId")]
    public string IdentifiantObjet { get; init; } = string.Empty;

    [JsonPropertyName("ItemGroupTypeId")]
    public string IdentifiantGroupeObjet { get; init; } = string.Empty;

    [JsonPropertyName("EnchantmentLevel")]
    public int NiveauEnchantement { get; init; }

    [JsonPropertyName("QualityLevel")]
    public int NiveauQualite { get; init; }

    [JsonPropertyName("Expires")]
    public string? DateExpiration { get; init; }
}

