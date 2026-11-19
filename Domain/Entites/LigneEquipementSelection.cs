using AlbionMarketSync.Domain.Enumerations;

namespace AlbionMarketSync.Domain.Entites;

/// <summary>
/// Représente une ligne d'équipement active lue depuis l'un des 6 sous-tableaux de l'onglet 'Sélection'.
/// </summary>
public sealed record LigneEquipementSelection(
    CategorieEquipement Categorie,
    string NomBrutSaisi,
    string PalierBrutSaisi,
    string EnchantementBrutSaisi,
    string CleBaseAlbion,
    int PalierTier,
    int NiveauEnchantement,
    string CoordonneeCellulePrixVente
);

