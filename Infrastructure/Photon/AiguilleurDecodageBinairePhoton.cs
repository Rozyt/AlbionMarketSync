using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Enumerations;

namespace AlbionMarketSync.Infrastructure.Photon;

/// <summary>
/// Aiguilleur de décodage binaire Photon (Module 2.1).
/// Dirige chaque trame vers :
/// 1) LecteurProtocole18Photon (GpBinaryV18 — Albion Online actuel)
/// 2) LecteurProtocole16Photon (GpBinaryV16 — Rétrocompatibilité)
/// 3) ExtracteurJsonDirectOctets (Filet de sécurité zero-allocation sur trames réassemblées)
/// </summary>
public static class AiguilleurDecodageBinairePhoton
{
    public static bool EssayerDecoderMessage(ReadOnlySpan<byte> octetsChargeUtile, out MessagePhotonDecode? messageDecode)
    {
        messageDecode = null;
        if (octetsChargeUtile.Length < 3)
            return false;

        int positionDepart = 0;
        byte premierOctet = octetsChargeUtile[0];
        byte typeMessageBrut;

        if (premierOctet is 0xF3 or 0xFD)
        {
            positionDepart = 1;
            typeMessageBrut = octetsChargeUtile[positionDepart++];
        }
        else if (premierOctet is 2 or 3 or 4 or 6 or 7)
        {
            typeMessageBrut = octetsChargeUtile[positionDepart++];
        }
        else
        {
            return false;
        }

        // Bit 7 (0x80) = message chiffré (ex: déplacements de joueurs). Les données de marché ne sont jamais chiffrées.
        if ((typeMessageBrut & 0x80) != 0)
            return false;

        var typeMessage = (TypeMessagePhoton)typeMessageBrut;

        if (LecteurProtocole18Photon.EssayerDecoder(octetsChargeUtile, positionDepart, typeMessage, out messageDecode))
        {
            ExtracteurJsonDirectOctets.GarantirExtractionOffresJson(octetsChargeUtile, messageDecode!);
            return true;
        }

        if (LecteurProtocole16Photon.EssayerDecoder(octetsChargeUtile, positionDepart, typeMessage, out messageDecode))
        {
            ExtracteurJsonDirectOctets.GarantirExtractionOffresJson(octetsChargeUtile, messageDecode!);
            return true;
        }

        if (ExtracteurJsonDirectOctets.ContientMarqueurPrixArgent(octetsChargeUtile))
        {
            string[] chainesJsonExtraites = ExtracteurJsonDirectOctets.ExtraireObjetsJsonDepuisOctets(octetsChargeUtile);
            if (chainesJsonExtraites.Length > 0)
            {
                byte codeOperation = positionDepart < octetsChargeUtile.Length ? octetsChargeUtile[positionDepart] : (byte)0;
                var parametresSecours = new Dictionary<byte, object?>
                {
                    [0] = chainesJsonExtraites
                };
                messageDecode = new MessagePhotonDecode(
                    TypeMessagePhoton.ReponseOperation,
                    codeOperation,
                    0,
                    null,
                    parametresSecours,
                    "DirectUtf8Span"
                );
                return true;
            }
        }

        return false;
    }
}

