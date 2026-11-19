using AlbionMarketSync.Application.Configuration;
using AlbionMarketSync.Application.Contrats;
using AlbionMarketSync.Domain.Entites;
using AlbionMarketSync.Domain.Enumerations;
using AlbionMarketSync.Domain.Interfaces;
using AlbionMarketSync.Domain.ServicesMetier;

namespace AlbionMarketSync.Application.CasUtilisation;

/// <summary>
/// Cas d'utilisation chargé de l'analyse des messages Photon décodés (Module 2) :
/// - Détecte les changements de ville (Join / ChangeCluster)
/// - Extrait les offres de l'Hôtel des Ventes (AuctionGetOffers / AuctionGetRequests)
/// - Découvre automatiquement les nouveaux codes d'opération lors des patchs d'Albion Online
/// - Calcule les résumés de prix via CalculateurPrixMarche.
/// </summary>
public sealed class ProcesseurPaquetsMarcheAlbion(
    ConfigurationApplication configurationApplication,
    ConfigurationCodesOperations configurationCodesOperations,
    IMoteurCartographieCellules moteurCartographie,
    IDeserialiseurOrdresMarche deserialiseurOrdres)
{
    public string VilleActuelle { get; set; } = "Ville auto-détectée (en attente)";
    public long NombreTotalPagesMarcheDecodees { get; private set; }
    public long NombreTotalOrdresExtraits { get; private set; }

    public event Action<IReadOnlyList<ResumeArticleMarche>, IReadOnlyList<OrdreMarcheAlbion>>? SurOrdresMarcheExtraits;
    public event Action<string, string>? SurVilleDetectee;
    public event Action<string, short>? SurCodeOperationDecouvert;

    /// <summary>
    /// Analyse un message Photon décodé : vérifie d'abord s'il contient un changement de ville/cluster,
    /// puis tente d'extraire les offres de l'Hôtel des Ventes s'il s'agit d'une réponse serveur.
    /// </summary>
    public void TraiterMessagePhoton(MessagePhotonDecode messagePhoton)
    {
        short? codeOperationInterne = ExtraireCodeOperationInterne(messagePhoton.ParametresMessage) ?? messagePhoton.CodeEnteteMessage;

        EssayerDetecterChangementVille(messagePhoton, codeOperationInterne);

        if (messagePhoton.TypeMessage is TypeMessagePhoton.ReponseOperation or TypeMessagePhoton.ReponseOperationInterne)
        {
            EssayerExtraireOrdresHotelDesVentes(messagePhoton, codeOperationInterne);
        }
    }

    /// <summary>
    /// Inspecte les paramètres du message pour détecter un code de cluster connu (0000=Thetford, 1000=Lymhurst, etc.)
    /// et mettre à jour automatiquement la ville active.
    /// </summary>
    private void EssayerDetecterChangementVille(MessagePhotonDecode messagePhoton, short? codeOperationInterne)
    {
        short codeOperationJoin = configurationCodesOperations.CodesOperations.GetValueOrDefault("Join", (short)2);
        short codeOperationChangementCluster = configurationCodesOperations.CodesOperations.GetValueOrDefault("ChangeCluster", (short)35);
        bool estOperationCluster = codeOperationInterne == codeOperationJoin || codeOperationInterne == codeOperationChangementCluster;

        foreach (var parametre in messagePhoton.ParametresMessage)
        {
            if (parametre.Value is string valeurTexte && valeurTexte.Length is >= 4 and <= 12)
            {
                if (configurationCodesOperations.CorrespondanceClusterVille.TryGetValue(valeurTexte, out string? nomVilleConnue))
                {
                    MettreAJourVilleSiNecessaire(nomVilleConnue, valeurTexte);
                    return;
                }

                if (estOperationCluster &&
                    valeurTexte.Length >= 4 &&
                    configurationCodesOperations.CorrespondanceClusterVille.TryGetValue(valeurTexte[..4], out string? nomVilleParPrefixe))
                {
                    MettreAJourVilleSiNecessaire(nomVilleParPrefixe, valeurTexte);
                    return;
                }
            }
        }
    }

    private void MettreAJourVilleSiNecessaire(string nouvelleVille, string identifiantCluster)
    {
        if (!string.Equals(VilleActuelle, nouvelleVille, StringComparison.Ordinal))
        {
            VilleActuelle = nouvelleVille;
            SurVilleDetectee?.Invoke(nouvelleVille, identifiantCluster);
        }
    }

    private void EssayerExtraireOrdresHotelDesVentes(MessagePhotonDecode messagePhoton, short? codeOperationInterne)
    {
        short codeOffresVente = configurationCodesOperations.CodesOperations.GetValueOrDefault("AuctionGetOffers", (short)75);
        short codeDemandesAchat = configurationCodesOperations.CodesOperations.GetValueOrDefault("AuctionGetRequests", (short)76);

        bool estCodeMarcheConfigure = codeOperationInterne == codeOffresVente || codeOperationInterne == codeDemandesAchat;
        if (!estCodeMarcheConfigure && !configurationCodesOperations.ActiverDecouverteAutomatiqueParSignature)
            return;

        IReadOnlyList<OrdreMarcheAlbion>? ordresExtraits = null;

        foreach (var parametre in messagePhoton.ParametresMessage)
        {
            if (parametre.Value is string[] tableauChaines && tableauChaines.Length > 0)
            {
                ordresExtraits = deserialiseurOrdres.EssayerExtraireListeOrdresJson(tableauChaines);
                if (ordresExtraits is { Count: > 0 })
                    break;
            }
            else if (parametre.Value is object?[] tableauObjets && tableauObjets.Length > 0 && tableauObjets[0] is string)
            {
                string[] chainesConverties = tableauObjets.OfType<string>().ToArray();
                ordresExtraits = deserialiseurOrdres.EssayerExtraireListeOrdresJson(chainesConverties);
                if (ordresExtraits is { Count: > 0 })
                    break;
            }
        }

        if (ordresExtraits is null || ordresExtraits.Count == 0)
            return;

        NombreTotalPagesMarcheDecodees++;
        NombreTotalOrdresExtraits += ordresExtraits.Count;

        if (codeOperationInterne.HasValue)
        {
            string typePremierOrdre = ordresExtraits[0].TypeEnchere;
            if (string.Equals(typePremierOrdre, "offer", StringComparison.OrdinalIgnoreCase) && codeOperationInterne.Value != codeOffresVente)
            {
                configurationCodesOperations.CodesOperations["AuctionGetOffers"] = codeOperationInterne.Value;
                SurCodeOperationDecouvert?.Invoke("AuctionGetOffers", codeOperationInterne.Value);
            }
            else if (string.Equals(typePremierOrdre, "request", StringComparison.OrdinalIgnoreCase) && codeOperationInterne.Value != codeDemandesAchat)
            {
                configurationCodesOperations.CodesOperations["AuctionGetRequests"] = codeOperationInterne.Value;
                SurCodeOperationDecouvert?.Invoke("AuctionGetRequests", codeOperationInterne.Value);
            }
        }

        double diviseurArgent = configurationApplication.CalculPrix.DiviseurArgent > 0
            ? configurationApplication.CalculPrix.DiviseurArgent
            : CalculateurPrixMarche.DiviseurArgentParDefaut;

        int nombreUnitesCibles = Math.Max(1, configurationApplication.CalculPrix.NombreUnitesMoyennePonderee);

        var resumesParArticle = ordresExtraits
            .Where(ordre => !string.IsNullOrWhiteSpace(ordre.IdentifiantObjet) && ordre.PrixUnitaireArgentBrut > 0 && ordre.QuantiteDisponible > 0)
            .GroupBy(ordre => (
                IdentifiantObjet: ordre.IdentifiantObjet.ToUpperInvariant(),
                ordre.PalierTier,
                ordre.NiveauEnchantement,
                ordre.NiveauQualite,
                TypeEnchere: ordre.TypeEnchere.ToLowerInvariant()
            ))
            .Select(groupeArticle =>
            {
                var offresTriees = CalculateurPrixMarche.TrierOffresParCompetitivite(groupeArticle, groupeArticle.Key.TypeEnchere).ToList();
                var (prixMinimum, prixMaximum, prixMoyenPondere, quantiteTotale) = CalculateurPrixMarche.CalculerIndicateursPrix(
                    offresTriees,
                    diviseurArgent,
                    nombreUnitesCibles);

                var (_, palierExtrait, enchantementExtrait) = MoteurCartographieCellules.AnalyserIdentifiantObjetAlbion(
                    groupeArticle.Key.IdentifiantObjet,
                    groupeArticle.Key.PalierTier,
                    groupeArticle.Key.NiveauEnchantement);

                string nomFrancaisObjet = moteurCartographie.ResoudreNomFrancais(
                    groupeArticle.Key.IdentifiantObjet,
                    palierExtrait,
                    enchantementExtrait);

                string? coordonneeCelluleCible = moteurCartographie.ResoudreCelluleCible(
                    groupeArticle.Key.IdentifiantObjet,
                    palierExtrait,
                    enchantementExtrait);

                return new ResumeArticleMarche(
                    IdentifiantObjet: groupeArticle.Key.IdentifiantObjet,
                    IdentifiantGroupeObjet: offresTriees[0].IdentifiantGroupeObjet,
                    NomFrancaisObjet: nomFrancaisObjet,
                    PalierTier: palierExtrait,
                    NiveauEnchantement: enchantementExtrait,
                    NiveauQualite: groupeArticle.Key.NiveauQualite,
                    TypeEnchere: groupeArticle.Key.TypeEnchere,
                    NombreOffresSurPage: offresTriees.Count,
                    QuantiteTotaleDisponible: quantiteTotale,
                    PrixUnitaireMinimumArgent: prixMinimum,
                    PrixUnitaireMaximumArgent: prixMaximum,
                    PrixMoyenPondereArgent: prixMoyenPondere,
                    VilleMarche: VilleActuelle,
                    CoordonneeCelluleSheetCible: coordonneeCelluleCible,
                    DateCaptureUtc: DateTime.UtcNow
                );
            })
            .ToList();

        if (resumesParArticle.Count > 0)
        {
            SurOrdresMarcheExtraits?.Invoke(resumesParArticle, ordresExtraits);
        }
    }

    private static short? ExtraireCodeOperationInterne(Dictionary<byte, object?> parametresMessage)
    {
        if (!parametresMessage.TryGetValue(253, out object? valeurBrute) || valeurBrute is null)
            return null;

        return valeurBrute switch
        {
            short entierCourt => entierCourt,
            byte octet => octet,
            int entier => (short)entier,
            long entierLong => (short)entierLong,
            _ => null
        };
    }
}

