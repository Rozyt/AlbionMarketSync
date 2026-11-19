using System.Buffers.Binary;
using System.Text;
using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Enumerations;

namespace AlbionMarketSync.Infrastructure.Photon;

/// <summary>
/// Lecteur binaire dédié au protocole ExitGames Photon Protocol16 (GpBinaryV16 — Rétrocompatibilité).
/// </summary>
public static class LecteurProtocole16Photon
{
    public static bool EssayerDecoder(
        ReadOnlySpan<byte> octetsTrame,
        int positionLecture,
        TypeMessagePhoton typeMessage,
        out MessagePhotonDecode? messageDecode)
    {
        messageDecode = null;
        try
        {
            switch (typeMessage)
            {
                case TypeMessagePhoton.RequeteOperation:
                case TypeMessagePhoton.RequeteOperationInterne:
                {
                    if (positionLecture + 3 > octetsTrame.Length) return false;
                    byte codeOperation = octetsTrame[positionLecture++];
                    var parametres = LireTableParametres(octetsTrame, ref positionLecture);
                    if (parametres is null) return false;
                    messageDecode = new MessagePhotonDecode(TypeMessagePhoton.RequeteOperation, codeOperation, null, null, parametres, "Protocol16");
                    return true;
                }

                case TypeMessagePhoton.ReponseOperation:
                case TypeMessagePhoton.ReponseOperationInterne:
                {
                    if (positionLecture + 4 > octetsTrame.Length) return false;
                    byte codeOperation = octetsTrame[positionLecture++];
                    short codeRetour = BinaryPrimitives.ReadInt16BigEndian(octetsTrame.Slice(positionLecture, 2));
                    positionLecture += 2;

                    byte typeMessageDebogage = octetsTrame[positionLecture++];
                    string? messageDebogage = LireValeurTypee(octetsTrame, ref positionLecture, typeMessageDebogage) as string;

                    var parametres = LireTableParametres(octetsTrame, ref positionLecture);
                    if (parametres is null) return false;
                    messageDecode = new MessagePhotonDecode(TypeMessagePhoton.ReponseOperation, codeOperation, codeRetour, messageDebogage, parametres, "Protocol16");
                    return true;
                }

                case TypeMessagePhoton.EvenementDonnees:
                {
                    if (positionLecture + 3 > octetsTrame.Length) return false;
                    byte codeEvenement = octetsTrame[positionLecture++];
                    var parametres = LireTableParametres(octetsTrame, ref positionLecture);
                    if (parametres is null) return false;
                    messageDecode = new MessagePhotonDecode(TypeMessagePhoton.EvenementDonnees, codeEvenement, null, null, parametres, "Protocol16");
                    return true;
                }

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<byte, object?>? LireTableParametres(ReadOnlySpan<byte> octetsTrame, ref int positionLecture)
    {
        if (positionLecture + 2 > octetsTrame.Length)
            return null;

        ushort nombreParametres = BinaryPrimitives.ReadUInt16BigEndian(octetsTrame.Slice(positionLecture, 2));
        positionLecture += 2;

        if (nombreParametres > 512)
            return null;

        var dictionnaireParametres = new Dictionary<byte, object?>(nombreParametres);
        for (int index = 0; index < nombreParametres; index++)
        {
            if (positionLecture + 2 > octetsTrame.Length)
                return null;

            byte cleParametre = octetsTrame[positionLecture++];
            byte codeTypeDonnee = octetsTrame[positionLecture++];
            object? valeurParametre = LireValeurTypee(octetsTrame, ref positionLecture, codeTypeDonnee);
            dictionnaireParametres[cleParametre] = valeurParametre;
        }

        return dictionnaireParametres;
    }

    private static object? LireValeurTypee(ReadOnlySpan<byte> octetsTrame, ref int positionLecture, byte codeTypeDonnee)
    {
        switch (codeTypeDonnee)
        {
            case 0:
            case (byte)'*':
                return null;

            case (byte)'b':
                if (positionLecture + 1 > octetsTrame.Length) throw new IndexOutOfRangeException();
                return octetsTrame[positionLecture++];

            case (byte)'o':
                if (positionLecture + 1 > octetsTrame.Length) throw new IndexOutOfRangeException();
                return octetsTrame[positionLecture++] != 0;

            case (byte)'k':
            {
                if (positionLecture + 2 > octetsTrame.Length) throw new IndexOutOfRangeException();
                short valeurCourt = BinaryPrimitives.ReadInt16BigEndian(octetsTrame.Slice(positionLecture, 2));
                positionLecture += 2;
                return valeurCourt;
            }

            case (byte)'i':
            {
                if (positionLecture + 4 > octetsTrame.Length) throw new IndexOutOfRangeException();
                int valeurEntier = BinaryPrimitives.ReadInt32BigEndian(octetsTrame.Slice(positionLecture, 4));
                positionLecture += 4;
                return valeurEntier;
            }

            case (byte)'l':
            {
                if (positionLecture + 8 > octetsTrame.Length) throw new IndexOutOfRangeException();
                long valeurLong = BinaryPrimitives.ReadInt64BigEndian(octetsTrame.Slice(positionLecture, 8));
                positionLecture += 8;
                return valeurLong;
            }

            case (byte)'f':
            {
                if (positionLecture + 4 > octetsTrame.Length) throw new IndexOutOfRangeException();
                float valeurFlottante = BinaryPrimitives.ReadSingleBigEndian(octetsTrame.Slice(positionLecture, 4));
                positionLecture += 4;
                return valeurFlottante;
            }

            case (byte)'d':
            {
                if (positionLecture + 8 > octetsTrame.Length) throw new IndexOutOfRangeException();
                double valeurDouble = BinaryPrimitives.ReadDoubleBigEndian(octetsTrame.Slice(positionLecture, 8));
                positionLecture += 8;
                return valeurDouble;
            }

            case (byte)'s':
                return LireChaineUtf8(octetsTrame, ref positionLecture);

            case (byte)'a':
            {
                if (positionLecture + 2 > octetsTrame.Length) throw new IndexOutOfRangeException();
                ushort nombreChaines = BinaryPrimitives.ReadUInt16BigEndian(octetsTrame.Slice(positionLecture, 2));
                positionLecture += 2;
                var tableauChaines = new string[nombreChaines];
                for (int index = 0; index < nombreChaines; index++)
                    tableauChaines[index] = LireChaineUtf8(octetsTrame, ref positionLecture);
                return tableauChaines;
            }

            case (byte)'x':
            {
                if (positionLecture + 4 > octetsTrame.Length) throw new IndexOutOfRangeException();
                int longueurOctets = BinaryPrimitives.ReadInt32BigEndian(octetsTrame.Slice(positionLecture, 4));
                positionLecture += 4;
                if (longueurOctets < 0 || positionLecture + longueurOctets > octetsTrame.Length) throw new IndexOutOfRangeException();
                byte[] tableauOctets = octetsTrame.Slice(positionLecture, longueurOctets).ToArray();
                positionLecture += longueurOctets;
                return tableauOctets;
            }

            case (byte)'n':
            {
                if (positionLecture + 4 > octetsTrame.Length) throw new IndexOutOfRangeException();
                int nombreEntiers = BinaryPrimitives.ReadInt32BigEndian(octetsTrame.Slice(positionLecture, 4));
                positionLecture += 4;
                if (nombreEntiers < 0 || positionLecture + nombreEntiers * 4 > octetsTrame.Length) throw new IndexOutOfRangeException();
                var tableauEntiers = new int[nombreEntiers];
                for (int index = 0; index < nombreEntiers; index++)
                {
                    tableauEntiers[index] = BinaryPrimitives.ReadInt32BigEndian(octetsTrame.Slice(positionLecture, 4));
                    positionLecture += 4;
                }
                return tableauEntiers;
            }

            case (byte)'y':
            {
                if (positionLecture + 3 > octetsTrame.Length) throw new IndexOutOfRangeException();
                ushort nombreElements = BinaryPrimitives.ReadUInt16BigEndian(octetsTrame.Slice(positionLecture, 2));
                positionLecture += 2;
                byte typeElement = octetsTrame[positionLecture++];
                var tableauObjets = new object?[nombreElements];
                for (int index = 0; index < nombreElements; index++)
                    tableauObjets[index] = LireValeurTypee(octetsTrame, ref positionLecture, typeElement);
                return tableauObjets;
            }

            case (byte)'z':
            {
                if (positionLecture + 2 > octetsTrame.Length) throw new IndexOutOfRangeException();
                ushort nombreElements = BinaryPrimitives.ReadUInt16BigEndian(octetsTrame.Slice(positionLecture, 2));
                positionLecture += 2;
                var tableauObjets = new object?[nombreElements];
                for (int index = 0; index < nombreElements; index++)
                {
                    byte typeElement = octetsTrame[positionLecture++];
                    tableauObjets[index] = LireValeurTypee(octetsTrame, ref positionLecture, typeElement);
                }
                return tableauObjets;
            }

            default:
                throw new NotSupportedException($"Protocol16 code type inconnu: {codeTypeDonnee}");
        }
    }

    private static string LireChaineUtf8(ReadOnlySpan<byte> octetsTrame, ref int positionLecture)
    {
        if (positionLecture + 2 > octetsTrame.Length)
            throw new IndexOutOfRangeException();

        ushort longueurOctets = BinaryPrimitives.ReadUInt16BigEndian(octetsTrame.Slice(positionLecture, 2));
        positionLecture += 2;
        if (longueurOctets == 0)
            return string.Empty;

        if (positionLecture + longueurOctets > octetsTrame.Length)
            throw new IndexOutOfRangeException();

        string chaineDecodee = Encoding.UTF8.GetString(octetsTrame.Slice(positionLecture, longueurOctets));
        positionLecture += longueurOctets;
        return chaineDecodee;
    }
}

