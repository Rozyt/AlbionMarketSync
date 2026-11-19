using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Enumerations;

namespace AlbionMarketSync.Domain.ServicesMetier;

/// <summary>
/// Service métier pur de normalisation mathématique des prix d'Albion Online (Module 2.2 &amp; Section 4.1).
/// Convertit les prix internes (diviseur 10 000) et calcule le prix minimum ainsi que la moyenne pondérée.
/// </summary>
public static class CalculateurPrixMarche
{
    public const double DiviseurArgentParDefaut = 10_000.0;

    /// <summary>
    /// Trie la liste d'offres par compétitivité : prix croissant pour les ventes ("offer"), décroissant pour les achats ("request").
    /// </summary>
    public static IEnumerable<OrdreMarcheAlbion> TrierOffresParCompetitivite(
        IEnumerable<OrdreMarcheAlbion> listeOffres,
        string typeEnchere)
    {
        // Offres de vente ("offer") : les plus compétitives sont les moins chères (tri croissant).
        // Ordres d'achat ("request") : les plus compétitifs sont les plus élevés (tri décroissant).
        return string.Equals(typeEnchere, "request", StringComparison.OrdinalIgnoreCase)
            ? listeOffres.OrderByDescending(offre => offre.PrixUnitaireArgentBrut)
            : listeOffres.OrderBy(offre => offre.PrixUnitaireArgentBrut);
    }

    /// <summary>
    /// Calcule les indicateurs statistiques d'une page d'offres : prix min, prix max, moyenne pondérée sur les N premières unités et quantité totale.
    /// </summary>
    public static (double PrixMinimum, double PrixMaximum, double PrixMoyenPondere, int QuantiteTotale) CalculerIndicateursPrix(
        IReadOnlyList<OrdreMarcheAlbion> offresTriees,
        double diviseurArgent = DiviseurArgentParDefaut,
        int nombreUnitesCiblesMoyennePonderee = 10)
    {
        if (offresTriees.Count == 0)
            return (0, 0, 0, 0);

        double diviseurEffectif = diviseurArgent > 0 ? diviseurArgent : DiviseurArgentParDefaut;
        int nombreUnitesCibles = Math.Max(1, nombreUnitesCiblesMoyennePonderee);

        double prixMinimum = offresTriees.Min(offre => offre.PrixUnitaireArgentBrut / diviseurEffectif);
        double prixMaximum = offresTriees.Max(offre => offre.PrixUnitaireArgentBrut / diviseurEffectif);
        int quantiteTotale = offresTriees.Sum(offre => offre.QuantiteDisponible);

        int quantiteUnitesRestantes = nombreUnitesCibles;
        double sommePondereePrix = 0;
        int quantiteUnitesCumulees = 0;

        foreach (var offre in offresTriees)
        {
            int quantiteRetenue = Math.Min(quantiteUnitesRestantes, offre.QuantiteDisponible);
            sommePondereePrix += quantiteRetenue * (offre.PrixUnitaireArgentBrut / diviseurEffectif);
            quantiteUnitesCumulees += quantiteRetenue;
            quantiteUnitesRestantes -= quantiteRetenue;
            if (quantiteUnitesRestantes <= 0)
                break;
        }

        double prixMoyenPondere = quantiteUnitesCumulees > 0
            ? Math.Round(sommePondereePrix / quantiteUnitesCumulees, 2)
            : prixMinimum;

        return (prixMinimum, prixMaximum, prixMoyenPondere, quantiteTotale);
    }

    public static double SelectionnerPrixReferenceArrondi(ResumeArticleMarche resumeArticle, StrategieCalculPrix strategieChoisie)
    {
        double prixBrut = strategieChoisie == StrategieCalculPrix.MoyennePonderee
            ? resumeArticle.PrixMoyenPondereArgent
            : resumeArticle.PrixUnitaireMinimumArgent;

        return Math.Round(prixBrut);
    }
}

