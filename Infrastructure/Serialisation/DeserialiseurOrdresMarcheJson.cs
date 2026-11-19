using System.Text.Json;
using AlbionMarketSync.Application.Contrats;
using AlbionMarketSync.Domain.Entites;

namespace AlbionMarketSync.Infrastructure.Serialisation;

/// <summary>
/// Désérialiseur haute performance des tableaux JSON d'offres de l'Hôtel des Ventes.
/// </summary>
public sealed class DeserialiseurOrdresMarcheJson : IDeserialiseurOrdresMarche
{
    public IReadOnlyList<OrdreMarcheAlbion>? EssayerExtraireListeOrdresJson(string[] chainesJsonCandidates)
    {
        if (chainesJsonCandidates.Length == 0)
            return null;

        string premiereChaine = chainesJsonCandidates[0];
        if (!premiereChaine.Contains("\"UnitPriceSilver\"", StringComparison.Ordinal) ||
            !premiereChaine.Contains("\"ItemTypeId\"", StringComparison.Ordinal))
        {
            return null;
        }

        var listeOrdres = new List<OrdreMarcheAlbion>(chainesJsonCandidates.Length);
        foreach (string texteJson in chainesJsonCandidates)
        {
            if (string.IsNullOrWhiteSpace(texteJson))
                continue;

            try
            {
                var ordre = JsonSerializer.Deserialize(texteJson, ContexteSerialisationJsonAlbion.Default.OrdreMarcheAlbion);
                if (ordre is not null && !string.IsNullOrEmpty(ordre.IdentifiantObjet))
                    listeOrdres.Add(ordre);
            }
            catch
            {
                // Ignorer une entrée JSON malformée sans interrompre le lot
            }
        }

        return listeOrdres;
    }
}

