using System.Collections.Concurrent;

namespace AlbionMarketSync.Domain.ServicesMetier;

/// <summary>
/// Service métier de consolidation Anti-Écrasement Page 2+ / Multi-Qualités (Section 4.1 du Cahier des Charges).
/// Conserve le prix minimum observé pour une même cellule durant une fenêtre glissante de recherche (3 minutes),
/// empêchant la consultation de la page 2 ou d'une qualité supérieure d'écraser le meilleur prix de la page 1.
/// </summary>
public sealed class ConsolidateurMeilleurPrixSession
{
    private readonly TimeSpan _dureeFenetreSession;
    private readonly ConcurrentDictionary<string, (double MeilleurPrixConstate, DateTime DebutFenetreUtc)> _meilleursPrixParCellule
        = new(StringComparer.OrdinalIgnoreCase);

    public ConsolidateurMeilleurPrixSession(TimeSpan? dureeFenetreSession = null)
    {
        _dureeFenetreSession = dureeFenetreSession ?? TimeSpan.FromMinutes(3);
    }

    public double ConsoliderMeilleurPrix(string coordonneeCellule, double prixCandidat, DateTime instantActuelUtc)
    {
        if (_meilleursPrixParCellule.TryGetValue(coordonneeCellule, out var entreeExistante))
        {
            if ((instantActuelUtc - entreeExistante.DebutFenetreUtc) <= _dureeFenetreSession)
            {
                if (prixCandidat < entreeExistante.MeilleurPrixConstate)
                {
                    _meilleursPrixParCellule[coordonneeCellule] = (prixCandidat, entreeExistante.DebutFenetreUtc);
                    return prixCandidat;
                }
                return entreeExistante.MeilleurPrixConstate;
            }
        }

        _meilleursPrixParCellule[coordonneeCellule] = (prixCandidat, instantActuelUtc);
        return prixCandidat;
    }

    public void ReinitialiserSession() => _meilleursPrixParCellule.Clear();
}

