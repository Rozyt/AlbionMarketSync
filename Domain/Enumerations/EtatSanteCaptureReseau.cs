namespace AlbionMarketSync.Domain.Enumerations;

/// <summary>
/// État de santé en temps réel du capteur réseau Npcap (Indicateurs 🟢 / 🟡 / 🔴 du Cahier des Charges).
/// </summary>
public enum EtatSanteCaptureReseau
{
    /// <summary>
    /// 🔴 Le pilote Npcap est absent ou aucune carte réseau n'a pu être ouverte.
    /// </summary>
    ErreurPiloteNpcap,

    /// <summary>
    /// 🟡 Le pilote Npcap écoute la carte réseau et attend des paquets d'Albion Online.
    /// </summary>
    EnEcouteAttenteJeu,

    /// <summary>
    /// 🟢 Le flux réseau UDP d'Albion Online est détecté et décodé en temps réel.
    /// </summary>
    CaptureTraficAlbionActive
}

