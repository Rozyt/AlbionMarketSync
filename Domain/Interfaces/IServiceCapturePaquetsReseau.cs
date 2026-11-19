using AlbionMarketSync.Domain.Enumerations;

namespace AlbionMarketSync.Domain.Interfaces;

/// <summary>
/// Contrat du service de capture réseau passive Npcap (Module 1).
/// </summary>
public interface IServiceCapturePaquetsReseau : IDisposable
{
    long NombreTotalPaquetsUdpCaptures { get; }

    DateTime? DateDernierPaquetRecuUtc { get; }

    string? NomCarteReseauActive { get; }

    EtatSanteCaptureReseau EtatSanteCapture { get; }

    event Action<byte[]>? SurPaquetUdpCapture;

    event Action<string>? SurMessageStatutReseau;

    /// <summary>Ouvre toutes les interfaces réseau actives avec un filtre BPF ciblant les ports UDP d'Albion Online.</summary>
    int DemarrerCaptureReseau();

    /// <summary>Ferme proprement les écouteurs Npcap sur toutes les cartes réseau.</summary>
    void ArreterCaptureReseau();
}

