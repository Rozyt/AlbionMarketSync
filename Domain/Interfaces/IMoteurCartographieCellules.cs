namespace AlbionMarketSync.Domain.Interfaces;

/// <summary>
/// Contrat du moteur de cartographie cellulaire ('Indice prix' Module 3.1 + 'Sélection' Module 3.2).
/// </summary>
public interface IMoteurCartographieCellules
{
    ICartographeOngletSelection CartographeSelection { get; }

    /// <summary>Résout toutes les cellules Google Sheets ('Indice prix' et 'Sélection') associées à un identifiant d'objet Albion.</summary>
    IReadOnlyList<string> ResoudreToutesLesCellulesCibles(string identifiantObjet, int palierBrut, int enchantementBrut);

    /// <summary>Résout la cellule principale de l'objet (ou null si l'objet n'est pas suivi dans le classeur).</summary>
    string? ResoudreCelluleCible(string identifiantObjet, int palierBrut, int enchantementBrut);

    /// <summary>Traduit un identifiant technique Albion (ex: T4_LEATHER_LEVEL1@1) en nom français lisible.</summary>
    string ResoudreNomFrancais(string identifiantObjet, int palierBrut, int enchantementBrut);
}

