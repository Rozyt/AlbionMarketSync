using AlbionMarketSync.Domain.Entites;

namespace AlbionMarketSync.Domain.Interfaces;

/// <summary>
/// Contrat du cartographe dynamique des 6 sous-tableaux de l'onglet 'Sélection'.
/// </summary>
public interface ICartographeOngletSelection
{
    IReadOnlyList<LigneEquipementSelection> LignesEquipementsActives { get; }

    /// <summary>Analyse la grille brute A1:Z60 de l'onglet 'Sélection' et extrait les équipements configurés par l'utilisateur.</summary>
    int ChargerDepuisGrilleSheet(IReadOnlyList<IReadOnlyList<string>> lignesGrille);

    /// <summary>Retourne les coordonnées des cellules 'Prix de vente' dans 'Sélection' correspondant à l'objet donné.</summary>
    IReadOnlyList<string> ResoudreCellulesSelection(string cleBaseAlbion, int palierTier, int niveauEnchantement);
}

