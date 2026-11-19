using System.Diagnostics;
using System.Windows;

namespace AlbionMarketSync.Presentation;

/// <summary>
/// Fenêtre de saisie des identifiants OAuth 2.0 Google (Client ID &amp; Client Secret)
/// permettant de lancer directement l'authentification GoogleWebAuthorizationBroker dans le navigateur.
/// </summary>
public partial class FenetreConfigurationOAuthGoogle : Window
{
    public string IdentifiantClientSaisi => ChampClientId.Text.Trim();
    public string SecretClientSaisi => ChampClientSecret.Text.Trim();

    public FenetreConfigurationOAuthGoogle(string clientIdInitial, string clientSecretInitial)
    {
        InitializeComponent();
        ChampClientId.Text = clientIdInitial;
        ChampClientSecret.Text = clientSecretInitial;
    }

    private void SurClicOuvrirConsoleGoogle(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://console.cloud.google.com/apis/credentials",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void SurClicValiderConnexion(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(IdentifiantClientSaisi) || string.IsNullOrWhiteSpace(SecretClientSaisi))
        {
            MessageBox.Show(
                this,
                "Veuillez renseigner le Client ID et le Client Secret OAuth 2.0 Google.",
                "Champs requis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void SurClicAnnuler(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
