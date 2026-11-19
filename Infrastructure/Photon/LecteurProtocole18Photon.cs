using System.Buffers.Binary;
using System.Text;
using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Enumerations;

namespace AlbionMarketSync.Infrastructure.Photon;

/// <summary>
/// Lecteur binaire dédié au protocole ExitGames Photon Protocol18 (GpBinaryV18 utilisé par Albion Online).
/// </summary>
public static class LecteurProtocole18Photon
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
                    if (positionLecture + 2 > octetsTrame.Length) return false;
                    byte codeOperation = octetsTrame[positionLecture++];
                    var parametres = LireTableParametres(octetsTrame, ref positionLecture);
                    if (parametres is null) return false;
                    messageDecode = new MessagePhotonDecode(TypeMessagePhoton.RequeteOperation, codeOperation, null, null, parametres, "Protocol18");
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
                    string? messageDebogage = LireValeurBinaire(octetsTrame, ref positionLecture, typeMessageDebogage) as string;

                    var parametres = LireTableParametres(octetsTrame, ref positionLecture);
                    if (parametres is null) return false;
                    messageDecode = new MessagePhotonDecode(TypeMessagePhoton.ReponseOperation, codeOperation, codeRetour, messageDebogage, parametres, "Protocol18");
                    return true;
                }

                case TypeMessagePhoton.EvenementDonnees:
                {
                    if (positionLecture + 2 > octetsTrame.Length) return false;
                    byte codeEvenement = octetsTrame[positionLecture++];
                    var parametres = LireTableParametres(octetsTrame, ref positionLecture);
                    if (parametres is null) return false;
                    messageDecode = new MessagePhotonDecode(TypeMessagePhoton.EvenementDonnees, codeEvenement, null, null, parametres, "Protocol18");
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
        uint nombreParametres = LireEntierNonSigneCompresse32(octetsTrame, ref positionLecture);
        if (nombreParametres > 512)
            return null;

        var dictionnaireParametres = new Dictionary<byte, object?>((int)nombreParametres);
        for (int index = 0; index < nombreParametres; index++)
        {
            if (positionLecture + 2 > octetsTrame.Length)
                return null;

            byte cleParametre = octetsTrame[positionLecture++];
            byte codeTypeDonnee = octetsTrame[positionLecture++];
            object? valeurParametre = LireValeurBinaire(octetsTrame, ref positionLecture, codeTypeDonnee);
            dictionnaireParametres[cleParametre] = valeurParametre;
        }

        return dictionnaireParametres;
    }

    private static object? LireValeurBinaire(ReadOnlySpan<byte> octetsTrame, ref int positionLecture, byte codeTypeDonnee)
    {
        if ((codeTypeDonnee & 0x40) != 0 && codeTypeDonnee != 0x40)
        {
            byte typeElementTableau = (byte)(codeTypeDonnee & 0x3F);
            return LireTableauType(octetsTrame, ref positionLecture, typeElementTableau);
        }

        switch (codeTypeDonnee)
        {
            case 0:
            case 8:
                return null;

            case 2:
                if (positionLecture + 1 > octetsTrame.Length) throw new IndexOutOfRangeException();
                return octetsTrame[positionLecture++] != 0;

            case 3:
                if (positionLecture + 1 > octetsTrame.Length) throw new IndexOutOfRangeException();
                return octetsTrame[positionLecture++];

            case 4:
            {
                if (positionLecture + 2 > octetsTrame.Length) throw new IndexOutOfRangeException();
                short valeurCourt = BinaryPrimitives.ReadInt16LittleEndian(octetsTrame.Slice(positionLecture, 2));
                positionLecture += 2;
                return valeurCourt;
            }

            case 5:
            {
                if (positionLecture + 4 > octetsTrame.Length) throw new IndexOutOfRangeException();
                float valeurFlottante = BinaryPrimitives.ReadSingleLittleEndian(octetsTrame.Slice(positionLecture, 4));
                positionLecture += 4;
                return valeurFlottante;
            }

            case 6:
            {
                if (positionLecture + 8 > octetsTrame.Length) throw new IndexOutOfRangeException();
                double valeurDouble = BinaryPrimitives.ReadDoubleLittleEndian(octetsTrame.Slice(positionLecture, 8));
                positionLecture += 8;
                return valeurDouble;
            }

            case 7:
                return LireChaineUtf8(octetsTrame, ref positionLecture);

            case 9:
                return LireEntierSigneCompresse32(octetsTrame, ref positionLecture);

            case 10:
                return LireEntierSigneCompresse64(octetsTrame, ref positionLecture);

            case 11:
                if (positionLecture + 1 > octetsTrame.Length) throw new IndexOutOfRangeException();
                return (int)octetsTrame[positionLecture++];

            case 12:
                if (positionLecture + 1 > octetsTrame.Length) throw new IndexOutOfRangeException();
                return -(int)octetsTrame[positionLecture++];

            case 13:
            {
                if (positionLecture + 2 > octetsTrame.Length) throw new IndexOutOfRangeException();
                int valeurEntierPositif = BinaryPrimitives.ReadUInt16LittleEndian(octetsTrame.Slice(positionLecture, 2));
                positionLecture += 2;
                return valeurEntierPositif;
            }

            case 14:
            {
                if (positionLecture + 2 > octetsTrame.Length) throw new IndexOutOfRangeException();
                int valeurEntierNegatif = BinaryPrimitives.ReadUInt16LittleEndian(octetsTrame.Slice(positionLecture, 2));
                positionLecture += 2;
                return -valeurEntierNegatif;
            }

            case 15:
                if (positionLecture + 1 > octetsTrame.Length) throw new IndexOutOfRangeException();
                return (long)octetsTrame[positionLecture++];

            case 16:
                if (positionLecture + 1 > octetsTrame.Length) throw new IndexOutOfRangeException();
                return -(long)octetsTrame[positionLecture++];

            case 17:
            {
                if (positionLecture + 2 > octetsTrame.Length) throw new IndexOutOfRangeException();
                long valeurLongPositif = BinaryPrimitives.ReadUInt16LittleEndian(octetsTrame.Slice(positionLecture, 2));
                positionLecture += 2;
                return valeurLongPositif;
            }

            case 18:
            {
                if (positionLecture + 2 > octetsTrame.Length) throw new IndexOutOfRangeException();
                long valeurLongNegatif = BinaryPrimitives.ReadUInt16LittleEndian(octetsTrame.Slice(positionLecture, 2));
                positionLecture += 2;
                return -valeurLongNegatif;
            }

            case 19:
            {
                if (positionLecture + 1 > octetsTrame.Length) throw new IndexOutOfRangeException();
                _ = octetsTrame[positionLecture++];
                uint longueurOctets = LireEntierNonSigneCompresse32(octetsTrame, ref positionLecture);
                if (positionLecture + (int)longueurOctets > octetsTrame.Length) throw new IndexOutOfRangeException();
                byte[] octetsPersonnalises = octetsTrame.Slice(positionLecture, (int)longueurOctets).ToArray();
                positionLecture += (int)longueurOctets;
                return octetsPersonnalises;
            }

            case 20:
            {
                if (positionLecture + 2 > octetsTrame.Length) throw new IndexOutOfRangeException();
                byte typeCle = octetsTrame[positionLecture++];
                byte typeValeur = octetsTrame[positionLecture++];
                uint nombreEntrees = LireEntierNonSigneCompresse32(octetsTrame, ref positionLecture);
                if (nombreEntrees > 4096) throw new IndexOutOfRangeException();
                var dictionnaire = new Dictionary<object, object?>((int)nombreEntrees);
                for (int index = 0; index < nombreEntrees; index++)
                {
                    byte typeCleEffectif = typeCle == 0 ? octetsTrame[positionLecture++] : typeCle;
                    object? cle = LireValeurBinaire(octetsTrame, ref positionLecture, typeCleEffectif);
                    byte typeValeurEffectif = typeValeur == 0 ? octetsTrame[positionLecture++] : typeValeur;
                    object? valeur = LireValeurBinaire(octetsTrame, ref positionLecture, typeValeurEffectif);
                    if (cle is not null)
                        dictionnaire[cle] = valeur;
                }
                return dictionnaire;
            }

            case 21:
            {
                uint nombreEntrees = LireEntierNonSigneCompresse32(octetsTrame, ref positionLecture);
                if (nombreEntrees > 4096) throw new IndexOutOfRangeException();
                var tableHachage = new Dictionary<object, object?>((int)nombreEntrees);
                for (int index = 0; index < nombreEntrees; index++)
                {
                    byte typeCle = octetsTrame[positionLecture++];
                    object? cle = LireValeurBinaire(octetsTrame, ref positionLecture, typeCle);
                    byte typeValeur = octetsTrame[positionLecture++];
                    object? valeur = LireValeurBinaire(octetsTrame, ref positionLecture, typeValeur);
                    if (cle is not null)
                        tableHachage[cle] = valeur;
                }
                return tableHachage;
            }

            case 23:
            {
                uint nombreElements = LireEntierNonSigneCompresse32(octetsTrame, ref positionLecture);
                if (nombreElements > 8192) throw new IndexOutOfRangeException();
                var tableauObjets = new object?[nombreElements];
                for (int index = 0; index < nombreElements; index++)
                {
                    byte typeElement = octetsTrame[positionLecture++];
                    tableauObjets[index] = LireValeurBinaire(octetsTrame, ref positionLecture, typeElement);
                }
                return tableauObjets;
            }

            case 24:
            {
                _ = octetsTrame[positionLecture++];
                return LireTableParametres(octetsTrame, ref positionLecture);
            }

            case 25:
            {
                _ = octetsTrame[positionLecture++];
                positionLecture += 2;
                byte typeDebogage = octetsTrame[positionLecture++];
                _ = LireValeurBinaire(octetsTrame, ref positionLecture, typeDebogage);
                return LireTableParametres(octetsTrame, ref positionLecture);
            }

            case 26:
            {
                _ = octetsTrame[positionLecture++];
                return LireTableParametres(octetsTrame, ref positionLecture);
            }

            case 27: return false;
            case 28: return true;
            case 29: return (short)0;
            case 30: return 0;
            case 31: return 0L;
            case 32: return 0f;
            case 33: return 0.0;
            case 34: return (byte)0;

            case 0x40:
            {
                uint nombreElements = LireEntierNonSigneCompresse32(octetsTrame, ref positionLecture);
                byte typeElement = octetsTrame[positionLecture++];
                return LireTableauTypeAvecTaille(octetsTrame, ref positionLecture, typeElement, (int)nombreElements);
            }

            default:
                throw new NotSupportedException($"Protocol18 code type inconnu: {codeTypeDonnee}");
        }
    }

    private static object LireTableauType(ReadOnlySpan<byte> octetsTrame, ref int positionLecture, byte typeElement)
    {
        uint nombreElements = LireEntierNonSigneCompresse32(octetsTrame, ref positionLecture);
        if (nombreElements > 65535)
            throw new IndexOutOfRangeException();

        return LireTableauTypeAvecTaille(octetsTrame, ref positionLecture, typeElement, (int)nombreElements);
    }

    private static object LireTableauTypeAvecTaille(ReadOnlySpan<byte> octetsTrame, ref int positionLecture, byte typeElement, int nombreElements)
    {
        if (typeElement == 3)
        {
            if (positionLecture + nombreElements > octetsTrame.Length) throw new IndexOutOfRangeException();
            byte[] tableauOctets = octetsTrame.Slice(positionLecture, nombreElements).ToArray();
            positionLecture += nombreElements;
            return tableauOctets;
        }

        if (typeElement == 7)
        {
            var tableauChaines = new string[nombreElements];
            for (int index = 0; index < nombreElements; index++)
                tableauChaines[index] = LireChaineUtf8(octetsTrame, ref positionLecture);
            return tableauChaines;
        }

        var tableauObjets = new object?[nombreElements];
        for (int index = 0; index < nombreElements; index++)
            tableauObjets[index] = LireValeurBinaire(octetsTrame, ref positionLecture, typeElement);
        return tableauObjets;
    }

    private static string LireChaineUtf8(ReadOnlySpan<byte> octetsTrame, ref int positionLecture)
    {
        uint longueurOctets = LireEntierNonSigneCompresse32(octetsTrame, ref positionLecture);
        if (longueurOctets == 0)
            return string.Empty;

        if (positionLecture + (int)longueurOctets > octetsTrame.Length)
            throw new IndexOutOfRangeException();

        string chaineDecodee = Encoding.UTF8.GetString(octetsTrame.Slice(positionLecture, (int)longueurOctets));
        positionLecture += (int)longueurOctets;
        return chaineDecodee;
    }

    /// <summary>
    /// Décode un entier non signé 32 bits compressé au format VarInt 7 bits (bit de poids fort = bit de continuation).
    /// </summary>
    private static uint LireEntierNonSigneCompresse32(ReadOnlySpan<byte> octetsTrame, ref int positionLecture)
    {
        uint resultat = 0;
        int decalageBits = 0;
        while (decalageBits != 35)
        {
            if (positionLecture >= octetsTrame.Length)
                throw new IndexOutOfRangeException();

            byte octetCourant = octetsTrame[positionLecture++];
            resultat |= (uint)(octetCourant & 0x7F) << decalageBits;
            decalageBits += 7;
            if ((octetCourant & 0x80) == 0)
                return resultat;
        }
        return resultat;
    }

    private static ulong LireEntierNonSigneCompresse64(ReadOnlySpan<byte> octetsTrame, ref int positionLecture)
    {
        ulong resultat = 0;
        int decalageBits = 0;
        while (decalageBits != 70)
        {
            if (positionLecture >= octetsTrame.Length)
                throw new IndexOutOfRangeException();

            byte octetCourant = octetsTrame[positionLecture++];
            resultat |= (ulong)(octetCourant & 0x7F) << decalageBits;
            decalageBits += 7;
            if ((octetCourant & 0x80) == 0)
                return resultat;
        }
        return resultat;
    }

    /// <summary>
    /// Décode un entier signé 32 bits encodé en ZigZag (utilisé par GpBinaryV18 pour compresser efficacement les petits nombres négatifs).
    /// </summary>
    private static int LireEntierSigneCompresse32(ReadOnlySpan<byte> octetsTrame, ref int positionLecture)
    {
        uint valeurZigZag = LireEntierNonSigneCompresse32(octetsTrame, ref positionLecture);
        return (int)((valeurZigZag >> 1) ^ (uint)-(int)(valeurZigZag & 1));
    }

    private static long LireEntierSigneCompresse64(ReadOnlySpan<byte> octetsTrame, ref int positionLecture)
    {
        ulong valeurZigZag = LireEntierNonSigneCompresse64(octetsTrame, ref positionLecture);
        return (long)((valeurZigZag >> 1) ^ (ulong)-(long)(valeurZigZag & 1));
    }
}

