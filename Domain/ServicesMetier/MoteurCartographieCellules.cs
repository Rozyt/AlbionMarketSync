using System.Collections.Frozen;
using System.Text.RegularExpressions;
using AlbionMarketSync.Domain.Interfaces;

namespace AlbionMarketSync.Domain.ServicesMetier;

/// <summary>
/// Moteur de cartographie cellulaire unifié (Module 3.1 'Indice prix' + Module 3.2 'Sélection').
/// Implémente IMoteurCartographieCellules avec FrozenDictionary (.NET 9) et validation systématique via GardeFouProtectionFormulesSheet.
/// </summary>
public sealed partial class MoteurCartographieCellules : IMoteurCartographieCellules
{
    private readonly FrozenDictionary<string, string> _dictionnaireNomsObjets;

    public ICartographeOngletSelection CartographeSelection { get; } = new CartographeOngletSelection();

    private static readonly FrozenDictionary<string, string> CellulesCoeursEtJetons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["T1_FACTION_STEPPE_TOKEN_1"] = "'Indice prix'!H13",
        ["T1_FACTION_FOREST_TOKEN_1"] = "'Indice prix'!I13",
        ["T1_FACTION_HIGHLAND_TOKEN_1"] = "'Indice prix'!J13",
        ["T1_FACTION_SWAMP_TOKEN_1"] = "'Indice prix'!K13",
        ["T1_FACTION_MOUNTAIN_TOKEN_1"] = "'Indice prix'!L13",
        ["T1_FACTION_CAERLEON_TOKEN_1"] = "'Indice prix'!M13",
        ["QUESTITEM_TOKEN_AVALON"] = "'Indice prix'!N13",
        ["QUESTITEM_TOKEN_MISTS"] = "'Indice prix'!O13",
        ["T4_SKILLBOOK_STANDARD"] = "'Indice prix'!H16",
        ["QUESTITEM_TOKEN_ROYAL_T4"] = "'Indice prix'!K16",
        ["QUESTITEM_TOKEN_ROYAL_T5"] = "'Indice prix'!L16",
        ["QUESTITEM_TOKEN_ROYAL_T6"] = "'Indice prix'!M16",
        ["QUESTITEM_TOKEN_ROYAL_T7"] = "'Indice prix'!N16",
        ["QUESTITEM_TOKEN_ROYAL_T8"] = "'Indice prix'!O16"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ColonnesRessourcesRaffinees = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["LEATHER"] = "B",
        ["CLOTH"] = "C",
        ["PLANKS"] = "D",
        ["METALBAR"] = "E"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ColonnesArmoiriesCapes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CAPEITEM_FW_LYMHURST_BP"] = "I",
        ["CAPEITEM_FW_BRIDGEWATCH_BP"] = "J",
        ["CAPEITEM_FW_MARTLOCK_BP"] = "K",
        ["CAPEITEM_FW_THETFORD_BP"] = "L",
        ["CAPEITEM_FW_FORTSTERLING_BP"] = "M",
        ["CAPEITEM_FW_CAERLEON_BP"] = "N",
        ["CAPEITEM_HERETIC_BP"] = "O",
        ["CAPEITEM_DEMON_BP"] = "P",
        ["CAPEITEM_KEEPER_BP"] = "Q",
        ["CAPEITEM_MORGANA_BP"] = "R",
        ["CAPEITEM_UNDEAD_BP"] = "S",
        ["CAPEITEM_SMUGGLER_BP"] = "T",
        ["CAPEITEM_AVALON_BP"] = "U",
        ["CAPEITEM_FW_BRECILIEN_BP"] = "V"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ColonnesArtefactsVestesCuir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ARTEFACT_ARMOR_LEATHER_MORGANA"] = "J",
        ["ARTEFACT_ARMOR_LEATHER_HELL"] = "K",
        ["ARTEFACT_ARMOR_LEATHER_UNDEAD"] = "L",
        ["ARTEFACT_ARMOR_LEATHER_FEY"] = "M",
        ["ARTEFACT_ARMOR_LEATHER_AVALON"] = "N"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ColonnesArtefactsRobesTissu = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ARTEFACT_ARMOR_CLOTH_KEEPER"] = "J",
        ["ARTEFACT_ARMOR_CLOTH_HELL"] = "K",
        ["ARTEFACT_ARMOR_CLOTH_MORGANA"] = "L",
        ["ARTEFACT_ARMOR_CLOTH_FEY"] = "M",
        ["ARTEFACT_ARMOR_CLOTH_AVALON"] = "N"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ColonnesArtefactsArmuresPlates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ARTEFACT_ARMOR_PLATE_UNDEAD"] = "J",
        ["ARTEFACT_ARMOR_PLATE_HELL"] = "K",
        ["ARTEFACT_ARMOR_PLATE_KEEPER"] = "L",
        ["ARTEFACT_ARMOR_PLATE_FEY"] = "M",
        ["ARTEFACT_ARMOR_PLATE_AVALON"] = "N"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public MoteurCartographieCellules(IDictionary<string, string> dictionnaireNomsObjets)
    {
        _dictionnaireNomsObjets = dictionnaireNomsObjets.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Décompose un identifiant technique d'Albion (ex: "T5_LEATHER_LEVEL2@2") en :
    /// - Clé de base ("LEATHER")
    /// - Palier / Tier (5)
    /// - Niveau d'enchantement (2)
    /// </summary>
    public static (string CleBaseAlbion, int PalierExtrait, int EnchantementExtrait) AnalyserIdentifiantObjetAlbion(
        string identifiantObjet,
        int palierParDefaut,
        int enchantementParDefaut)
    {
        if (string.IsNullOrWhiteSpace(identifiantObjet))
            return (string.Empty, palierParDefaut, enchantementParDefaut);

        string identifiantTravail = identifiantObjet.Trim().ToUpperInvariant();
        int palier = palierParDefaut;
        int enchantement = enchantementParDefaut;

        int indexArobase = identifiantTravail.IndexOf('@');
        if (indexArobase > 0)
        {
            if (int.TryParse(identifiantTravail.AsSpan(indexArobase + 1), out int enchantementArobase))
                enchantement = enchantementArobase;
            identifiantTravail = identifiantTravail[..indexArobase];
        }

        var correspondanceLevel = ExpressionSuffixeLevel().Match(identifiantTravail);
        if (correspondanceLevel.Success)
        {
            if (int.TryParse(correspondanceLevel.Groups[1].ValueSpan, out int enchantementLevel))
                enchantement = enchantementLevel;
            identifiantTravail = identifiantTravail[..correspondanceLevel.Index];
        }

        if (!CellulesCoeursEtJetons.ContainsKey(identifiantTravail) &&
            identifiantTravail.Length > 3 &&
            identifiantTravail[0] == 'T' &&
            char.IsDigit(identifiantTravail[1]) &&
            identifiantTravail[2] == '_')
        {
            palier = identifiantTravail[1] - '0';
            identifiantTravail = identifiantTravail[3..];
        }

        return (identifiantTravail, palier, enchantement);
    }

    public IReadOnlyList<string> ResoudreToutesLesCellulesCibles(string identifiantObjet, int palierBrut, int enchantementBrut)
    {
        if (string.IsNullOrWhiteSpace(identifiantObjet))
            return Array.Empty<string>();

        string? celluleIndicePrix = ResoudreCelluleIndicePrix(identifiantObjet, palierBrut, enchantementBrut);
        if (celluleIndicePrix is not null && !GardeFouProtectionFormulesSheet.EstCelluleProtegee(celluleIndicePrix))
            return [celluleIndicePrix];

        var (cleBaseAlbion, palier, enchantement) = AnalyserIdentifiantObjetAlbion(identifiantObjet, palierBrut, enchantementBrut);
        var cellulesSelection = CartographeSelection.ResoudreCellulesSelection(cleBaseAlbion, palier, enchantement);
        if (cellulesSelection.Count > 0)
        {
            return cellulesSelection.Where(cellule => !GardeFouProtectionFormulesSheet.EstCelluleProtegee(cellule)).ToList();
        }

        return Array.Empty<string>();
    }

    public string? ResoudreCelluleCible(string identifiantObjet, int palierBrut, int enchantementBrut)
    {
        var cellules = ResoudreToutesLesCellulesCibles(identifiantObjet, palierBrut, enchantementBrut);
        return cellules.Count > 0 ? string.Join(", ", cellules) : null;
    }

    private static string? ResoudreCelluleIndicePrix(string identifiantObjet, int palierBrut, int enchantementBrut)
    {
        string identifiantCompletNormalise = identifiantObjet.Trim().ToUpperInvariant();
        int indexArobase = identifiantCompletNormalise.IndexOf('@');
        if (indexArobase > 0)
            identifiantCompletNormalise = identifiantCompletNormalise[..indexArobase];

        if (CellulesCoeursEtJetons.TryGetValue(identifiantCompletNormalise, out string? celluleDirecte))
            return celluleDirecte;

        var (cleBaseAlbion, palier, enchantement) = AnalyserIdentifiantObjetAlbion(identifiantObjet, palierBrut, enchantementBrut);
        string cleBaseNormalisee = cleBaseAlbion.Replace("ARTIFACT_", "ARTEFACT_", StringComparison.OrdinalIgnoreCase);

        if (ColonnesRessourcesRaffinees.TryGetValue(cleBaseNormalisee, out string? colonneRessource))
        {
            int numeroLigne = palier switch
            {
                2 => 6,
                3 => 7,
                >= 4 and <= 8 when enchantement is >= 0 and <= 4 => 8 + (palier - 4) * 5 + enchantement,
                _ => -1
            };

            if (numeroLigne is >= 6 and <= 32)
                return $"'Indice prix'!{colonneRessource}{numeroLigne}";
        }

        if (ColonnesArmoiriesCapes.TryGetValue(cleBaseNormalisee, out string? colonneArmoirie) && palier is >= 4 and <= 8)
        {
            return $"'Indice prix'!{colonneArmoirie}{palier + 1}";
        }

        if (ColonnesArtefactsVestesCuir.TryGetValue(cleBaseNormalisee, out string? colonneArtefactCuir) && palier is >= 4 and <= 8)
        {
            return $"'Indice prix'!{colonneArtefactCuir}{17 + palier}";
        }

        if (ColonnesArtefactsRobesTissu.TryGetValue(cleBaseNormalisee, out string? colonneArtefactTissu) && palier is >= 4 and <= 8)
        {
            return $"'Indice prix'!{colonneArtefactTissu}{25 + palier}";
        }

        if (ColonnesArtefactsArmuresPlates.TryGetValue(cleBaseNormalisee, out string? colonneArtefactPlates) && palier is >= 4 and <= 8)
        {
            return $"'Indice prix'!{colonneArtefactPlates}{33 + palier}";
        }

        return null;
    }

    public string ResoudreNomFrancais(string identifiantObjet, int palierBrut, int enchantementBrut)
    {
        if (_dictionnaireNomsObjets.TryGetValue(identifiantObjet, out string? nomExact))
            return nomExact;

        var (cleBaseAlbion, palier, enchantement) = AnalyserIdentifiantObjetAlbion(identifiantObjet, palierBrut, enchantementBrut);
        string cleBaseNormalisee = cleBaseAlbion.Replace("ARTIFACT_", "ARTEFACT_", StringComparison.OrdinalIgnoreCase);

        if (_dictionnaireNomsObjets.TryGetValue(cleBaseNormalisee, out string? nomBase))
        {
            string suffixePalier = palier > 0 ? $" T{palier}" : string.Empty;
            string suffixeEnchantement = enchantement > 0 ? $".{enchantement}" : string.Empty;
            return $"{nomBase}{suffixePalier}{suffixeEnchantement}";
        }

        return identifiantObjet;
    }

    [GeneratedRegex(@"_LEVEL(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex ExpressionSuffixeLevel();
}

