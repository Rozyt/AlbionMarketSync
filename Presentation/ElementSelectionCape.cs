using System.ComponentModel;
using AlbionMarketSync.Domain.Entites;

namespace AlbionMarketSync.Presentation;

/// <summary>
/// Élément de sélection d'une Cape avec case à cocher pour l'interface graphique WPF.
/// </summary>
public sealed class ElementSelectionCape : INotifyPropertyChanged
{
    private bool _estSelectionnee;

    public RecetteCraftCape Recette { get; }

    public string NomAffichage => Recette.NomAffichageCape;

    public string DetailIngredients => $"Armoirie col. {Recette.LettreColonneArmoirieIndicePrix} + {Recette.NomRechercheCoeurJeu}";

    public bool EstSelectionnee
    {
        get => _estSelectionnee;
        set
        {
            if (_estSelectionnee != value)
            {
                _estSelectionnee = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EstSelectionnee)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ElementSelectionCape(RecetteCraftCape recette, bool estSelectionneeParDefaut = false)
    {
        Recette = recette;
        _estSelectionnee = estSelectionneeParDefaut;
    }
}
