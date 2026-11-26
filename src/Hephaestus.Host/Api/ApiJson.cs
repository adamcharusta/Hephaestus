using System.Text.Json.Serialization;

namespace Hephaestus.Host.Api;

/// <summary>The version document, the same shape on the API and in <c>--json</c> output.</summary>
internal sealed record VersionDocument(string Version);

/// <summary>An RFC 9457 problem details document. <c>type</c> is never translated; <c>title</c> and <c>detail</c> are.</summary>
internal sealed record ProblemDocument(string Type, string Title, int Status, string Detail);

/// <summary>Source-generated JSON: camelCase keys, as every Hephaestus document.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VersionDocument))]
[JsonSerializable(typeof(ProblemDocument))]
internal sealed partial class ApiJson : JsonSerializerContext;
