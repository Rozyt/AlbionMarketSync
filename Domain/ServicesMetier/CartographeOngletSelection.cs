using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Enumerations;
using AlbionMarketSync.Domain.Interfaces;

namespace AlbionMarketSync.Domain.ServicesMetier;

/// <summary>
/// Cartographe dynamique de l'onglet 'Sélection' (gid=0) conforme au Module 3.2 du Cahier des Charges.
/// Lit les 6 sous-tableaux de 'Sélection' et associe chaque équipement aux colonnes 'Prix de vente' (F, O ou Y).
/// </summary>
public sealed class CartographeOngletSelection : ICartographeOngletSelection
{
    private readonly Dictionary<(string CleBaseAlbion, int PalierTier, int NiveauEnchantement), List<string>> _tableCellulesParEquipement = new();
    private readonly List<LigneEquipementSelection> _lignesEquipementsActives = new();
    private readonly Lock _verrouSynchronisation = new();

    private static readonly FrozenDictionary<string, string> SynonymesCapes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["bridgewatch"] = "CAPEITEM_FW_BRIDGEWATCH",
        ["bridge"] = "CAPEITEM_FW_BRIDGEWATCH",
        ["demoniaque"] = "CAPEITEM_DEMON",
        ["demon"] = "CAPEITEM_DEMON",
        ["lymhurst"] = "CAPEITEM_FW_LYMHURST",
        ["lym"] = "CAPEITEM_FW_LYMHURST",
        ["heretique"] = "CAPEITEM_HERETIC",
        ["martlock"] = "CAPEITEM_FW_MARTLOCK",
        ["guardien"] = "CAPEITEM_KEEPER",
        ["gardien"] = "CAPEITEM_KEEPER",
        ["thetford"] = "CAPEITEM_FW_THETFORD",
        ["morgana"] = "CAPEITEM_MORGANA",
        ["fortsterling"] = "CAPEITEM_FW_FORTSTERLING",
        ["mortsvivant"] = "CAPEITEM_UNDEAD",
        ["mortsvivants"] = "CAPEITEM_UNDEAD",
        ["mortvivant"] = "CAPEITEM_UNDEAD",
        ["caerleon"] = "CAPEITEM_FW_CAERLEON",
        ["contrebandier"] = "CAPEITEM_SMUGGLER",
        ["ava"] = "CAPEITEM_AVALON",
        ["avalon"] = "CAPEITEM_AVALON",
        ["avalonienne"] = "CAPEITEM_AVALON",
        ["brecilien"] = "CAPEITEM_FW_BRECILIEN",
        ["cape"] = "CAPE",
        ["classique"] = "CAPE",
        ["capeclassique"] = "CAPE"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> SynonymesSacs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sacclassique"] = "BAG",
        ["classique"] = "BAG",
        ["sac"] = "BAG",
        ["normal"] = "BAG",
        ["sacperspicacite"] = "BAG_INSIGHT",
        ["perspicacite"] = "BAG_INSIGHT",
        ["clairvoyance"] = "BAG_INSIGHT",
        ["sacdeclairvoyance"] = "BAG_INSIGHT"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> SynonymesVestesCuir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["mercenaire"] = "ARMOR_LEATHER_SET1",
        ["chasseur"] = "ARMOR_LEATHER_SET2",
        ["assassin"] = "ARMOR_LEATHER_SET3",
        ["royal"] = "ARMOR_LEATHER_ROYAL",
        ["royale"] = "ARMOR_LEATHER_ROYAL",
        ["traqueur"] = "ARMOR_LEATHER_MORGANA",
        ["tourmenteur"] = "ARMOR_LEATHER_HELL",
        ["spectre"] = "ARMOR_LEATHER_UNDEAD",
        ["tenacite"] = "ARMOR_LEATHER_AVALON",
        ["marcheursdesbrumes"] = "ARMOR_LEATHER_FEY",
        ["marcheurdesbrumes"] = "ARMOR_LEATHER_FEY",
        ["brumes"] = "ARMOR_LEATHER_FEY"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> SynonymesRobesTissu = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["erudit"] = "ARMOR_CLOTH_SET1",
        ["eecclesiastique"] = "ARMOR_CLOTH_SET2",
        ["ecclesiastique"] = "ARMOR_CLOTH_SET2",
        ["clerc"] = "ARMOR_CLOTH_SET2",
        ["mage"] = "ARMOR_CLOTH_SET3",
        ["royal"] = "ARMOR_CLOTH_ROYAL",
        ["royale"] = "ARMOR_CLOTH_ROYAL",
        ["druide"] = "ARMOR_CLOTH_KEEPER",
        ["monstre"] = "ARMOR_CLOTH_HELL",
        ["demon"] = "ARMOR_CLOTH_HELL",
        ["culte"] = "ARMOR_CLOTH_MORGANA",
        ["sectaire"] = "ARMOR_CLOTH_MORGANA",
        ["feerique"] = "ARMOR_CLOTH_FEY",
        ["ecaillesfeeriques"] = "ARMOR_CLOTH_FEY",
        ["purete"] = "ARMOR_CLOTH_AVALON"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> SynonymesArmuresPlates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["soldat"] = "ARMOR_PLATE_SET1",
        ["chevalier"] = "ARMOR_PLATE_SET2",
        ["guardien"] = "ARMOR_PLATE_SET3",
        ["gardien"] = "ARMOR_PLATE_SET3",
        ["royal"] = "ARMOR_PLATE_ROYAL",
        ["royale"] = "ARMOR_PLATE_ROYAL",
        ["gardetombe"] = "ARMOR_PLATE_UNDEAD",
        ["demon"] = "ARMOR_PLATE_HELL",
        ["judicateur"] = "ARMOR_PLATE_KEEPER",
        ["crepusculaire"] = "ARMOR_PLATE_FEY",
        ["tissecrepuscule"] = "ARMOR_PLATE_FEY",
        ["bravoure"] = "ARMOR_PLATE_AVALON"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LigneEquipementSelection> LignesEquipementsActives
    {
        get
        {
            lock (_verrouSynchronisation)
                return _lignesEquipementsActives.ToList();
        }
    }

    /// <summary>
    /// Parcourt les 6 sous-tableaux de l'onglet 'Sélection' (Capes, Sacs, Vestes en cuir x2, Robes en tissu, Armures en plates)
    /// et construit la table de correspondance (Clé Albion, Tier, Enchantement) -> Cellule Prix de vente.
    /// </summary>
    public int ChargerDepuisGrilleSheet(IReadOnlyList<IReadOnlyList<string>> lignesGrille)
    {
        lock (_verrouSynchronisation)
        {
            _tableCellulesParEquipement.Clear();
            _lignesEquipementsActives.Clear();

            // 1. Capes (Colonnes A..F, lignes 4 à 24 -> Prix en F)
            AnalyserSousTableau(lignesGrille, ligneDebutBase1: 4, ligneFinBase1: 24, indexColonneDepartBase0: 0, lettreColonnePrixCible: "F", CategorieEquipement.Cape, SynonymesCapes);
            // 2. Sacs (Colonnes A..F, lignes 29 à 36 -> Prix en F)
            AnalyserSousTableau(lignesGrille, ligneDebutBase1: 29, ligneFinBase1: 36, indexColonneDepartBase0: 0, lettreColonnePrixCible: "F", CategorieEquipement.Sac, SynonymesSacs);
            // 3. Vestes en cuir - Tableau 1 (Colonnes J..O, lignes 4 à 18 -> Prix en O)
            AnalyserSousTableau(lignesGrille, ligneDebutBase1: 4, ligneFinBase1: 18, indexColonneDepartBase0: 9, lettreColonnePrixCible: "O", CategorieEquipement.VesteCuir, SynonymesVestesCuir);
            // 4. Vestes en cuir - Tableau 2 (Colonnes T..Y, lignes 4 à 18 -> Prix en Y)
            AnalyserSousTableau(lignesGrille, ligneDebutBase1: 4, ligneFinBase1: 18, indexColonneDepartBase0: 19, lettreColonnePrixCible: "Y", CategorieEquipement.VesteCuir, SynonymesVestesCuir);
            // 5. Robes en tissu (Colonnes J..O, lignes 23 à 36 -> Prix en O)
            AnalyserSousTableau(lignesGrille, ligneDebutBase1: 23, ligneFinBase1: 36, indexColonneDepartBase0: 9, lettreColonnePrixCible: "O", CategorieEquipement.RobeTissu, SynonymesRobesTissu);
            // 6. Armures en plates (Colonnes J..O, lignes 40 à 54 -> Prix en O)
            AnalyserSousTableau(lignesGrille, ligneDebutBase1: 40, ligneFinBase1: 54, indexColonneDepartBase0: 9, lettreColonnePrixCible: "O", CategorieEquipement.ArmurePlates, SynonymesArmuresPlates);

            return _lignesEquipementsActives.Count;
        }
    }

    public IReadOnlyList<string> ResoudreCellulesSelection(string cleBaseAlbion, int palierTier, int niveauEnchantement)
    {
        lock (_verrouSynchronisation)
        {
            if (_tableCellulesParEquipement.TryGetValue((cleBaseAlbion.ToUpperInvariant(), palierTier, niveauEnchantement), out var cellulesTrouvees))
                return cellulesTrouvees.ToList();
            return Array.Empty<string>();
        }
    }

    private void AnalyserSousTableau(
        IReadOnlyList<IReadOnlyList<string>> lignesGrille,
        int ligneDebutBase1,
        int ligneFinBase1,
        int indexColonneDepartBase0,
        string lettreColonnePrixCible,
        CategorieEquipement categorieEquipement,
        FrozenDictionary<string, string> dictionnaireSynonymes)
    {
        for (int numeroLigneBase1 = ligneDebutBase1; numeroLigneBase1 <= ligneFinBase1; numeroLigneBase1++)
        {
            int indexLigneBase0 = numeroLigneBase1 - 1;
            if (indexLigneBase0 < 0 || indexLigneBase0 >= lignesGrille.Count)
                continue;

            var cellulesLigne = lignesGrille[indexLigneBase0];
            string nomSaisiBrut = ObtenirValeurCellule(cellulesLigne, indexColonneDepartBase0);
            string palierSaisiBrut = ObtenirValeurCellule(cellulesLigne, indexColonneDepartBase0 + 1);
            string enchantementSaisiBrut = ObtenirValeurCellule(cellulesLigne, indexColonneDepartBase0 + 2);

            if (string.IsNullOrWhiteSpace(nomSaisiBrut) || string.IsNullOrWhiteSpace(palierSaisiBrut))
                continue;

            string cleFrancaisNormalisee = NormaliserLibelleFrancais(nomSaisiBrut);
            if (!dictionnaireSynonymes.TryGetValue(cleFrancaisNormalisee, out string? cleBaseAlbion))
                continue;

            if (!EssayerExtrairePalierEtEnchantement(palierSaisiBrut, enchantementSaisiBrut, out int palierTier, out int niveauEnchantement))
                continue;

            string coordonneeCelluleCible = $"'Sélection'!{lettreColonnePrixCible}{numeroLigneBase1}";
            var ligneEquipement = new LigneEquipementSelection(
                categorieEquipement,
                nomSaisiBrut.Trim(),
                palierSaisiBrut.Trim(),
                enchantementSaisiBrut.Trim(),
                cleBaseAlbion,
                palierTier,
                niveauEnchantement,
                coordonneeCelluleCible);

            _lignesEquipementsActives.Add(ligneEquipement);

            var cleRecherche = (cleBaseAlbion.ToUpperInvariant(), palierTier, niveauEnchantement);
            if (!_tableCellulesParEquipement.TryGetValue(cleRecherche, out var listeCellules))
            {
                listeCellules = new List<string>();
                _tableCellulesParEquipement[cleRecherche] = listeCellules;
            }
            if (!listeCellules.Contains(coordonneeCelluleCible))
                listeCellules.Add(coordonneeCelluleCible);
        }
    }

    private static string ObtenirValeurCellule(IReadOnlyList<string> cellulesLigne, int indexColonne)
        => indexColonne >= 0 && indexColonne < cellulesLigne.Count ? cellulesLigne[indexColonne] ?? string.Empty : string.Empty;

    private static bool EssayerExtrairePalierEtEnchantement(string palierBrut, string enchantementBrut, out int palierTier, out int niveauEnchantement)
    {
        palierTier = 0;
        niveauEnchantement = 0;

        string textePalier = palierBrut.Trim().ToUpperInvariant().Replace(',', '.');
        if (textePalier.StartsWith('T'))
            textePalier = textePalier[1..];

        int indexPoint = textePalier.IndexOf('.');
        if (indexPoint > 0)
        {
            if (!int.TryParse(textePalier.AsSpan(0, indexPoint), out palierTier))
                return false;
            _ = int.TryParse(textePalier.AsSpan(indexPoint + 1), out niveauEnchantement);
        }
        else
        {
            if (!int.TryParse(textePalier, out palierTier))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(enchantementBrut))
        {
            string texteEnchantement = enchantementBrut.Trim().Replace(',', '.').TrimStart('.');
            if (int.TryParse(texteEnchantement, out int enchantementAnalyse))
                niveauEnchantement = enchantementAnalyse;
        }

        return palierTier is >= 2 and <= 8 && niveauEnchantement is >= 0 and <= 4;
    }

    public static string NormaliserLibelleFrancais(string texteEntree)
    {
        if (string.IsNullOrWhiteSpace(texteEntree))
            return string.Empty;

        string texteDecompose = texteEntree.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var constructeurTexte = new StringBuilder(texteDecompose.Length);

        foreach (char caractere in texteDecompose)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(caractere))
                constructeurTexte.Append(caractere);
        }

        return constructeurTexte.ToString();
    }
}

