using AlbionMarketSync.Domain.Entites;

namespace AlbionMarketSync.Domain.Interfaces;

/// <summary>
/// Contrat de l'analyseur de trames UDP ExitGames Photon avec réassemblage des paquets fragmentés (Module 2).
/// </summary>
public interface IAnalyseurPaquetsPhoton
{
    long NombreTotalPaquetsPhoton { get; }

    long NombreTotalCommandesTraitees { get; }

    long NombreTotalFragmentsRecus { get; }

    long NombreTotalMessagesFragmentesReassembles { get; }

    long NombreTotalMessagesDecodes { get; }

    IReadOnlyDictionary<byte, int> CompteurParTypeCommande { get; }

    /// <summary>Déclenché chaque fois qu'un message Photon complet (simple ou réassemblé) est décodé.</summary>
    event Action<MessagePhotonDecode>? SurMessagePhotonDecode;

    /// <summary>Analyse la charge utile brute d'une trame UDP capturée sur les ports d'Albion Online.</summary>
    void TraiterChargeUtileUdp(ReadOnlySpan<byte> donneesUdpBrutes);
}

