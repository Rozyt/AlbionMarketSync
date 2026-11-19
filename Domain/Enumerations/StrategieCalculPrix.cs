namespace AlbionMarketSync.Domain.Enumerations;

/// <summary>
/// Stratégies de calcul du prix de référence pour Google Sheets (Section 4.1 du Cahier des Charges).
/// </summary>
public enum StrategieCalculPrix
{
    /// <summary>
    /// Prend le prix de vente unitaire le plus bas constaté sur la première page.
    /// </summary>
    PrixVenteMinimum,

    /// <summary>
    /// Calcule la moyenne pondérée sur les N premières unités les moins chères (anti-manipulation à 1 unité).
    /// </summary>
    MoyennePonderee
}

