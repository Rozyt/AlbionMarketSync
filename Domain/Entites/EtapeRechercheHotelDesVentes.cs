namespace AlbionMarketSync.Domain.Entites;

/// <summary>
/// Représente une requête de recherche précise que le joueur doit effectuer dans l'Hôtel des Ventes
/// pour alimenter une cellule requise de l'onglet 'Indice prix'.
/// </summary>
public sealed record EtapeRechercheHotelDesVentes(
    int NumeroOrdre,
    string CategorieIngredient,
    string NomObjetAChercherEnJeu,
    string FiltrePalierTier,
    string FiltreEnchantement,
    string CoordonneeCelluleIndicePrix,
    double PrixActuelDansIndicePrix,
    bool EstRemplieDansIndicePrix,
    bool EstActualiseeDurantSession,
    bool EstIgnoreeManuellement
)
{
    public string StatutTextuel =>
        EstIgnoreeManuellement ? "⏭️ Ignorée"
        : EstActualiseeDurantSession ? "✅ Actualisée (Session)"
        : EstRemplieDansIndicePrix ? $"☑️ Présente ({PrixActuelDansIndicePrix:N0} Ag)"
        : "⚠️ Manquante (0 Ag)";

    public string InstructionComplete =>
        $"Chercher « {NomObjetAChercherEnJeu} »  |  Tier : {FiltrePalierTier}  |  Enchantement : {FiltreEnchantement}";
}
