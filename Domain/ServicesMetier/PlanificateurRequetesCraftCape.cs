using System.Globalization;
using AlbionMarketSync.Domain.Entites;

namespace AlbionMarketSync.Domain.ServicesMetier;

/// <summary>
/// Service métier qui calcule la liste exacte des cellules de 'Indice prix' nécessaires
/// selon les Capes choisies par le joueur, le Niveau (Tier) Max et l'Enchantement Max,
/// puis inspecte la feuille 'Indice prix' pour indiquer la prochaine requête à faire à l'Hôtel des Ventes.
/// </summary>
public static class PlanificateurRequetesCraftCape
{
    /// <summary>
    /// Catalogue officiel des 14 Capes de l'onglet 'Cout de craft' avec leurs colonnes exactes dans 'Indice prix'.
    /// </summary>
    public static IReadOnlyList<RecetteCraftCape> CatalogueCapesDisponibles { get; } =
    [
        new("lymhurst",      "Cape de Lymhurst",      "Armoiries de Lymhurst",      "I", "Sylvecœur",           "'Indice prix'!I13"),
        new("bridgewatch",   "Cape de Bridgewatch",   "Armoiries de Bridgewatch",   "J", "Fauvecœur",           "'Indice prix'!H13"),
        new("martlock",      "Cape de Martlock",      "Armoiries de Martlock",      "K", "Rochecœur",           "'Indice prix'!J13"),
        new("thetford",      "Cape de Thetford",      "Armoiries de Thetford",      "L", "Vignecœur",           "'Indice prix'!K13"),
        new("fortsterling",  "Cape de Fort Sterling", "Armoiries de Fort Sterling", "M", "Cimecœur",            "'Indice prix'!L13"),
        new("caerleon",      "Cape de Caerleon",      "Armoiries de Caerleon",      "N", "Ombrecœur",           "'Indice prix'!M13"),
        new("heretique",     "Cape d'Hérétique",      "Armoiries d'Hérétique",      "O", "Sylvecœur",           "'Indice prix'!I13"),
        new("demoniaque",    "Cape Démoniaque",       "Armoiries Démoniaques",      "P", "Fauvecœur",           "'Indice prix'!H13"),
        new("guardien",      "Cape de Gardien",       "Armoiries de Gardien",       "Q", "Rochecœur",           "'Indice prix'!J13"),
        new("morgana",       "Cape de Morgana",       "Armoiries de Morgana",       "R", "Vignecœur",           "'Indice prix'!K13"),
        new("mortsvivant",   "Cape de Mort-vivant",   "Armoiries de Mort-vivant",   "S", "Cimecœur",            "'Indice prix'!L13"),
        new("contrebandier", "Cape de Contrebandier", "Armoiries de Contrebandier", "T", "Ombrecœur",           "'Indice prix'!M13"),
        new("ava",           "Cape Avalonienne",      "Armoiries Avaloniennes",     "U", "Énergie avalonienne", "'Indice prix'!N13"),
        new("brecilien",     "Cape de Brecilien",     "Armoiries de Brecilien",     "V", "Feu de fée",          "'Indice prix'!O13")
    ];

    /// <summary>
    /// Construit le plan complet des requêtes à faire dans l'Hôtel des Ventes en fonction des choix du joueur
    /// et des valeurs actuellement présentes dans la feuille 'Indice prix'.
    /// </summary>
    public static IReadOnlyList<EtapeRechercheHotelDesVentes> ConstruirePlanRequetes(
        IReadOnlyList<RecetteCraftCape> capesSelectionnees,
        int palierTierMinimum,
        int palierTierMaximum,
        int enchantementMaximum,
        IReadOnlyList<IReadOnlyList<string>> grilleIndicePrix,
        IReadOnlySet<string> cellulesActualiseesSession,
        IReadOnlySet<string> cellulesIgnoreesManuellement)
    {
        if (capesSelectionnees.Count == 0)
            return Array.Empty<EtapeRechercheHotelDesVentes>();

        int tierMin = Math.Clamp(palierTierMinimum, 4, 8);
        int tierMax = Math.Clamp(Math.Max(tierMin, palierTierMaximum), 4, 8);
        int enchantMax = Math.Clamp(enchantementMaximum, 0, 4);

        var etapesPlanifiees = new List<EtapeRechercheHotelDesVentes>();
        var cellulesDejaAjoutees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int numeroOrdre = 1;

        void AjouterEtapeSiNouvelle(
            string categorieIngredient,
            string nomObjetJeu,
            string filtreTier,
            string filtreEnchantement,
            string coordonneeCellule)
        {
            if (!cellulesDejaAjoutees.Add(coordonneeCellule))
                return;

            double prixActuel = LirePrixNumeriqueDansGrilleIndicePrix(grilleIndicePrix, coordonneeCellule);
            bool estRemplie = prixActuel > 0;
            bool estActualiseeSession = cellulesActualiseesSession.Contains(coordonneeCellule);
            bool estIgnoree = cellulesIgnoreesManuellement.Contains(coordonneeCellule);

            etapesPlanifiees.Add(new EtapeRechercheHotelDesVentes(
                NumeroOrdre: numeroOrdre++,
                CategorieIngredient: categorieIngredient,
                NomObjetAChercherEnJeu: nomObjetJeu,
                FiltrePalierTier: filtreTier,
                FiltreEnchantement: filtreEnchantement,
                CoordonneeCelluleIndicePrix: coordonneeCellule,
                PrixActuelDansIndicePrix: prixActuel,
                EstRemplieDansIndicePrix: estRemplie,
                EstActualiseeDurantSession: estActualiseeSession,
                EstIgnoreeManuellement: estIgnoree));
        }

        // 1. Cuir raffiné (Colonne B de 'Indice prix') pour chaque (Tier, Enchantement) demandé
        for (int tier = tierMin; tier <= tierMax; tier++)
        {
            for (int enchant = 0; enchant <= enchantMax; enchant++)
            {
                int ligne = 8 + (tier - 4) * 5 + enchant;
                string coordonnee = $"'Indice prix'!B{ligne}";
                AjouterEtapeSiNouvelle(
                    categorieIngredient: "1. Ressource — Cuir raffiné",
                    nomObjetJeu: "Cuir",
                    filtreTier: $"T{tier}",
                    filtreEnchantement: $".{enchant}",
                    coordonneeCellule: coordonnee);
            }
        }

        // 2. Tissu raffiné (Colonne C de 'Indice prix') pour chaque (Tier, Enchantement) demandé
        for (int tier = tierMin; tier <= tierMax; tier++)
        {
            for (int enchant = 0; enchant <= enchantMax; enchant++)
            {
                int ligne = 8 + (tier - 4) * 5 + enchant;
                string coordonnee = $"'Indice prix'!C{ligne}";
                AjouterEtapeSiNouvelle(
                    categorieIngredient: "2. Ressource — Tissu raffiné",
                    nomObjetJeu: "Tissu",
                    filtreTier: $"T{tier}",
                    filtreEnchantement: $".{enchant}",
                    coordonneeCellule: coordonnee);
            }
        }

        // 3. Armoiries de chaque Cape sélectionnée (Colonnes I à V, lignes 5 à 9 — toujours enchantement .0)
        foreach (var recette in capesSelectionnees)
        {
            for (int tier = tierMin; tier <= tierMax; tier++)
            {
                int ligneArmoirie = tier + 1;
                string coordonnee = $"'Indice prix'!{recette.LettreColonneArmoirieIndicePrix}{ligneArmoirie}";
                AjouterEtapeSiNouvelle(
                    categorieIngredient: $"3. Armoiries — {recette.NomAffichageCape}",
                    nomObjetJeu: recette.NomRechercheArmoirieJeu,
                    filtreTier: $"T{tier}",
                    filtreEnchantement: "Tous (.0)",
                    coordonneeCellule: coordonnee);
            }
        }

        // 4. Cœurs de faction / Énergie / Feu de fée (Ligne 13 de 'Indice prix')
        foreach (var recette in capesSelectionnees)
        {
            AjouterEtapeSiNouvelle(
                categorieIngredient: "4. Cœur / Jeton de Faction",
                nomObjetJeu: recette.NomRechercheCoeurJeu,
                filtreTier: "Tous (T1/T4)",
                filtreEnchantement: "Tous (.0)",
                coordonneeCellule: recette.CoordonneeCelluleCoeurIndicePrix);
        }

        return etapesPlanifiees;
    }

    /// <summary>
    /// Détermine la prochaine requête à faire dans l'Hôtel des Ventes.
    /// - Si exigerActualisationSession = false : cible en priorité les cellules manquantes (0 / vides / na) dans 'Indice prix'.
    /// - Si exigerActualisationSession = true  : cible toutes les cellules non encore actualisées durant la session.
    /// </summary>
    public static EtapeRechercheHotelDesVentes? DeterminerProchaineRequete(
        IReadOnlyList<EtapeRechercheHotelDesVentes> planRequetes,
        bool exigerActualisationSession)
    {
        if (planRequetes.Count == 0)
            return null;

        // 1. Priorité absolue : toute cellule requise dont le prix dans 'Indice prix' est 0, vide ou 'na'
        var premiereManquante = planRequetes.FirstOrDefault(etape =>
            !etape.EstIgnoreeManuellement && !etape.EstRemplieDansIndicePrix);

        if (premiereManquante is not null)
            return premiereManquante;

        // 2. Si le joueur souhaite actualiser tous les prix de la recette durant cette session
        if (exigerActualisationSession)
        {
            return planRequetes.FirstOrDefault(etape =>
                !etape.EstIgnoreeManuellement && !etape.EstActualiseeDurantSession);
        }

        return null;
    }

    /// <summary>
    /// Lit la valeur numérique d'une coordonnée de type "'Indice prix'!B8" ou "'Indice prix'!I5" dans la grille 2D.
    /// </summary>
    public static double LirePrixNumeriqueDansGrilleIndicePrix(
        IReadOnlyList<IReadOnlyList<string>> grilleIndicePrix,
        string coordonneeCellule)
    {
        if (!EssayerConvertirCoordonneeEnIndicesZero(coordonneeCellule, out int indexLigne0, out int indexColonne0))
            return 0;

        if (indexLigne0 < 0 || indexLigne0 >= grilleIndicePrix.Count)
            return 0;

        var ligne = grilleIndicePrix[indexLigne0];
        if (indexColonne0 < 0 || indexColonne0 >= ligne.Count)
            return 0;

        string texteBrut = ligne[indexColonne0];
        if (string.IsNullOrWhiteSpace(texteBrut))
            return 0;

        string texteNettoye = texteBrut
            .Replace("\u00A0", string.Empty)
            .Replace("\u202F", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(',', '.')
            .Trim();

        return double.TryParse(texteNettoye, NumberStyles.Any, CultureInfo.InvariantCulture, out double valeur) && valeur > 0
            ? valeur
            : 0;
    }

    public static bool EssayerConvertirCoordonneeEnIndicesZero(
        string coordonneeCellule,
        out int indexLigneBase0,
        out int indexColonneBase0)
    {
        indexLigneBase0 = -1;
        indexColonneBase0 = -1;

        if (string.IsNullOrWhiteSpace(coordonneeCellule))
            return false;

        int indexPointExclamation = coordonneeCellule.IndexOf('!');
        ReadOnlySpan<char> locale = indexPointExclamation >= 0
            ? coordonneeCellule.AsSpan(indexPointExclamation + 1)
            : coordonneeCellule.AsSpan();

        int longueurLettres = 0;
        while (longueurLettres < locale.Length && char.IsLetter(locale[longueurLettres]))
            longueurLettres++;

        if (longueurLettres == 0 || longueurLettres >= locale.Length)
            return false;

        int indexCol = 0;
        for (int i = 0; i < longueurLettres; i++)
        {
            indexCol = indexCol * 26 + (char.ToUpperInvariant(locale[i]) - 'A' + 1);
        }
        indexColonneBase0 = indexCol - 1;

        if (!int.TryParse(locale[longueurLettres..], out int numeroLigneBase1) || numeroLigneBase1 < 1)
            return false;

        indexLigneBase0 = numeroLigneBase1 - 1;
        return true;
    }
}
