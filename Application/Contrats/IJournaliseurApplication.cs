namespace AlbionMarketSync.Application.Contrats;

public enum NiveauSeveriteJournal
{
    Information,
    Succes,
    Avertissement,
    Erreur,
    MiseEnEvidence
}

/// <summary>
/// Contrat de journalisation applicative découplé de la Console et de la future interface WPF.
/// </summary>
public interface IJournaliseurApplication
{
    void Journaliser(string message, NiveauSeveriteJournal niveauSeverite = NiveauSeveriteJournal.Information);

    void Informer(string message) => Journaliser(message, NiveauSeveriteJournal.Information);
    void Succes(string message) => Journaliser(message, NiveauSeveriteJournal.Succes);
    void Avertir(string message) => Journaliser(message, NiveauSeveriteJournal.Avertissement);
    void Erreur(string message) => Journaliser(message, NiveauSeveriteJournal.Erreur);
    void MettreEnEvidence(string message) => Journaliser(message, NiveauSeveriteJournal.MiseEnEvidence);
}

