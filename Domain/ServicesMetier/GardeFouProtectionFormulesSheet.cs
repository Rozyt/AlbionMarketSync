namespace AlbionMarketSync.Domain.ServicesMetier;

/// <summary>
/// Garde-fou strict d'intégrité cellulaire (Critère d'acceptation T-04 du Cahier des Charges).
/// Interdit toute écriture sur :
/// - L'onglet 'Cout de craft' (composé à 100 % de formules)
/// - Les cellules 'Indice prix'!I21:I41 (formules =K16 à =O16 des Sceaux royaux)
/// - Les colonnes de formules ('Cout de craft', 'Bénef unitaire', 'Benef total') de l'onglet 'Sélection'.
/// </summary>
public static class GardeFouProtectionFormulesSheet
{
    public static bool EstCelluleProtegee(string coordonneeCellule)
    {
        if (string.IsNullOrWhiteSpace(coordonneeCellule))
            return true;

        if (coordonneeCellule.Contains("Cout de craft", StringComparison.OrdinalIgnoreCase))
            return true;

        // Protection 'Indice prix'!I21:I41 (formules =K16 à =O16 pour les Sceaux royaux)
        if (coordonneeCellule.StartsWith("'Indice prix'!I2", StringComparison.OrdinalIgnoreCase) ||
            coordonneeCellule.StartsWith("'Indice prix'!I3", StringComparison.OrdinalIgnoreCase) ||
            coordonneeCellule.StartsWith("'Indice prix'!I4", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Dans 'Sélection', seules les colonnes F, O et Y (Prix de vente) sont autorisées en écriture
        if (coordonneeCellule.StartsWith("'Sélection'!", StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<char> coordonneeLocale = coordonneeCellule.AsSpan("'Sélection'!".Length);
            if (coordonneeLocale.IsEmpty || (coordonneeLocale[0] != 'F' && coordonneeLocale[0] != 'O' && coordonneeLocale[0] != 'Y'))
                return true;
        }

        return false;
    }
}

