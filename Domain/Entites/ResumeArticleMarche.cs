using System.Text.Json.Serialization;

namespace AlbionMarketSync.Domain.Entites;

/// <summary>
/// Résumé calculé d'un article sur une page de l'Hôtel des Ventes (prix minimum, moyenne pondérée, quantité et cellule cible).
/// </summary>
public sealed record ResumeArticleMarche(
    [property: JsonPropertyName("ItemTypeId")] string IdentifiantObjet,
    [property: JsonPropertyName("ItemGroupTypeId")] string IdentifiantGroupeObjet,
    [property: JsonPropertyName("FriendlyName")] string NomFrancaisObjet,
    [property: JsonPropertyName("Tier")] int PalierTier,
    [property: JsonPropertyName("Enchantment")] int NiveauEnchantement,
    [property: JsonPropertyName("Quality")] int NiveauQualite,
    [property: JsonPropertyName("AuctionType")] string TypeEnchere,
    [property: JsonPropertyName("OfferCount")] int NombreOffresSurPage,
    [property: JsonPropertyName("TotalAmountAvailable")] int QuantiteTotaleDisponible,
    [property: JsonPropertyName("MinUnitPriceSilver")] double PrixUnitaireMinimumArgent,
    [property: JsonPropertyName("MaxUnitPriceSilver")] double PrixUnitaireMaximumArgent,
    [property: JsonPropertyName("WeightedAvgTopPriceSilver")] double PrixMoyenPondereArgent,
    [property: JsonPropertyName("City")] string VilleMarche,
    [property: JsonPropertyName("TargetSheetCell")] string? CoordonneeCelluleSheetCible,
    [property: JsonPropertyName("TimestampUtc")] DateTime DateCaptureUtc
);

