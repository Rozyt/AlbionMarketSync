namespace AlbionMarketSync.Domain.Entites;

/// <summary>
/// Définit les ingrédients exacts requis dans l'onglet 'Indice prix' pour fabriquer une Cape donnée
/// selon les formules de l'onglet 'Cout de craft'.
/// </summary>
public sealed record RecetteCraftCape(
    string CodeCapeInterne,
    string NomAffichageCape,
    string NomRechercheArmoirieJeu,
    string LettreColonneArmoirieIndicePrix,
    string NomRechercheCoeurJeu,
    string CoordonneeCelluleCoeurIndicePrix
);
