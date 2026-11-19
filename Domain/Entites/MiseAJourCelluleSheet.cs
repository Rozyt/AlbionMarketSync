namespace AlbionMarketSync.Domain.Entites;

/// <summary>
/// Représente une demande d'écriture validée, consolidée et dédupliquée vers une cellule précise de Google Sheets.
/// </summary>
public sealed record MiseAJourCelluleSheet(
    string CoordonneeCellule,
    string IdentifiantObjet,
    string NomFrancaisObjet,
    double PrixArgentArrondi,
    string VilleMarche,
    DateTime DateMiseEnFileUtc
);

