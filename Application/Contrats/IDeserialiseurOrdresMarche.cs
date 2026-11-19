using AlbionMarketSync.Domain.Entites;

namespace AlbionMarketSync.Application.Contrats;

/// <summary>
/// Contrat de désérialisation haute performance des tableaux JSON d'offres de l'Hôtel des Ventes.
/// </summary>
public interface IDeserialiseurOrdresMarche
{
    IReadOnlyList<OrdreMarcheAlbion>? EssayerExtraireListeOrdresJson(string[] chainesJsonCandidates);
}

