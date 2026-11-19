namespace AlbionMarketSync.Domain.Interfaces;

/// <summary>
/// Contrat du cache persistant de déduplication cellulaire et d'historique des prix (Module 4.2).
/// </summary>
public interface ICachePrixMarcheLocal : IDisposable
{
    /// <summary>Vérifie si le nouveau prix diffère de la dernière valeur envoyée pour cette cellule.</summary>
    bool LePrixDeLaCelluleAChange(string coordonneeCellule, double nouveauPrixArgent, out double? ancienPrixEnCache);

    /// <summary>Mémorise en base locale qu'une cellule a été synchronisée avec succès vers Google Sheets.</summary>
    void EnregistrerPrixCelluleSynchronise(string coordonneeCellule, string identifiantObjet, double prixArgent, string villeMarche);

    /// <summary>Archive une observation de marché dans l'historique SQLite local.</summary>
    void EnregistrerHistoriqueMarche(
        string identifiantObjet,
        int palierTier,
        int niveauEnchantement,
        int niveauQualite,
        string typeEnchere,
        double prixMinimumArgent,
        double prixMoyenPondereArgent,
        int quantiteTotale,
        string villeMarche,
        string? coordonneeCelluleCible);

    int ObtenirNombreCellulesEnCache();

    /// <summary>Purge du cache les cellules qui ont été remises à zéro ou vidées manuellement dans Google Sheets.</summary>
    void SynchroniserCacheAvecFeuilleIndicePrix(IReadOnlyList<IReadOnlyList<string>> grilleIndicePrix);
}

