using System.Text.Json.Serialization;

namespace Hephaestus.Core.Results;

/// <summary>
/// Source-generated JSON of the result documents (AD-13): camelCase keys, and a key the record does not have is an error, so that a result is read strictly and
/// never interpreted when it is not what the schema describes.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ImplementationResult))]
public sealed partial class ResultJson : JsonSerializerContext;
