using System.Text;
using AlbionMarketSync.Application.Contrats;

namespace AlbionMarketSync.Infrastructure.Journalisation;

/// <summary>
/// Journaliseur multi-thread écrivant simultanément en couleur dans la Console et dans le fichier live_capture.log.
/// </summary>
public sealed class JournaliseurConsoleEtFichier : IJournaliseurApplication, IDisposable
{
    private readonly FileStream _fluxFichierJournal;
    private readonly StreamWriter _ecrivainTexte;
    private readonly Lock _verrouEcriture = new();

    public event Action<string, NiveauSeveriteJournal>? SurNouvelleEntreeJournal;

    public JournaliseurConsoleEtFichier(string cheminFichierJournal)
    {
        _fluxFichierJournal = new FileStream(cheminFichierJournal, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        _ecrivainTexte = new StreamWriter(_fluxFichierJournal, Encoding.UTF8) { AutoFlush = true };
    }

    public void Journaliser(string message, NiveauSeveriteJournal niveauSeverite = NiveauSeveriteJournal.Information)
    {
        lock (_verrouEcriture)
        {
            ConsoleColor? couleurConsole = niveauSeverite switch
            {
                NiveauSeveriteJournal.Succes => ConsoleColor.Green,
                NiveauSeveriteJournal.Avertissement => ConsoleColor.Yellow,
                NiveauSeveriteJournal.Erreur => ConsoleColor.Red,
                NiveauSeveriteJournal.MiseEnEvidence => ConsoleColor.Cyan,
                _ => null
            };

            if (couleurConsole.HasValue)
                Console.ForegroundColor = couleurConsole.Value;

            Console.WriteLine(message);

            if (couleurConsole.HasValue)
                Console.ResetColor();

            Console.Out.Flush();
            _ecrivainTexte.WriteLine(message);
        }

        SurNouvelleEntreeJournal?.Invoke(message, niveauSeverite);
    }

    public void Dispose()
    {
        lock (_verrouEcriture)
        {
            _ecrivainTexte.Dispose();
            _fluxFichierJournal.Dispose();
        }
    }
}

