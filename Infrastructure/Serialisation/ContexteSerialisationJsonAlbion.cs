using System.Text.Json.Serialization;
using AlbionMarketSync.Application.Configuration;
using AlbionMarketSync.Domain.Entites;

namespace AlbionMarketSync.Infrastructure.Serialisation;

public sealed record OperationEcritureLotGoogleSheet(
    [property: JsonPropertyName("op")] string TypeOperation,
    [property: JsonPropertyName("range")] string PlageCellule,
    [property: JsonPropertyName("data")] long[][] ValeursCellule
);

/// <summary>
/// Contexte de génération de code System.Text.Json (.NET 9 / C# 13) pour une sérialisation sans réflexion.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OrdreMarcheAlbion))]
[JsonSerializable(typeof(ResumeArticleMarche))]
[JsonSerializable(typeof(ConfigurationApplication))]
[JsonSerializable(typeof(ConfigurationCodesOperations))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[][]))]
[JsonSerializable(typeof(List<OperationEcritureLotGoogleSheet>))]
public partial class ContexteSerialisationJsonAlbion : JsonSerializerContext
{
}

