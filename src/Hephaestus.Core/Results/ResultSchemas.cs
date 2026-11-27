using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Results;

/// <summary>
/// The JSON schema of a step kind's result (AD-13), generated from the Core record, as <c>result.schema.json</c> carries it and as an adapter adapts it to its CLI. It is
/// restricted to what the schema-constrained output of both CLIs accepts (Story 1.1): objects with every property required and no other, strings, enumerations, and
/// arrays, and no reference, alternative, or nullable type.
/// </summary>
public static class ResultSchemas
{
    /// <summary>The keywords a schema may use; a generated schema that holds another is a defect and is refused.</summary>
    public static IReadOnlySet<string> AllowedKeywords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "type", "properties", "required", "additionalProperties", "items", "enum", "description",
    };

    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = (_, schema) =>
        {
            if (schema is not JsonObject node)
            {
                return schema;
            }

            if (node["properties"] is JsonObject properties)
            {
                node["required"] = new JsonArray([.. properties.Select(property => (JsonNode)property.Key)]);
                node["additionalProperties"] = false;
            }

            // The CLIs' constrained output reads an enumeration of strings as a string, so the type is stated.
            if (node.ContainsKey("enum") && !node.ContainsKey("type"))
            {
                node.Insert(0, "type", "string");
            }

            return schema;
        },
    };

    private static readonly Lazy<string> Implementation = new(() => Generate(ResultJson.Default.ImplementationResult));

    /// <summary>
    /// The schema of the result of a step of this kind, as one compact JSON object; null for a kind whose result type is not defined yet. The same text is written for every
    /// Agent, so that the file does not tell them apart.
    /// </summary>
    public static string? For(StepKind kind) => kind switch
    {
        StepKind.Implementation or StepKind.Correction or StepKind.Rework => Implementation.Value,
        _ => null,
    };

    /// <summary>The keywords a schema holds that <see cref="AllowedKeywords"/> does not list, by their path; empty for a schema both CLIs accept.</summary>
    public static IReadOnlyList<string> Violations(string schema)
    {
        var violations = new List<string>();
        Walk(JsonNode.Parse(schema), "$", violations);
        return violations;
    }

    private static string Generate(JsonTypeInfo type)
    {
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(type, ExporterOptions);
        var violations = Violations(schema.ToJsonString());
        return violations.Count == 0
            ? schema.ToJsonString()
            : throw new InvalidOperationException($"The schema of {type.Type.Name} uses what the Agent CLIs do not accept: {string.Join(", ", violations)}.");
    }

    private static void Walk(JsonNode? node, string path, List<string> violations)
    {
        if (node is not JsonObject schema)
        {
            return;
        }

        foreach (var (key, value) in schema)
        {
            if (!AllowedKeywords.Contains(key))
            {
                violations.Add($"{path}.{key}");
                continue;
            }

            switch (key)
            {
                case "properties" when value is JsonObject properties:
                    foreach (var (name, child) in properties)
                    {
                        Walk(child, $"{path}.properties.{name}", violations);
                    }

                    break;
                case "items":
                    Walk(value, $"{path}.items", violations);
                    break;
                case "type" when value is JsonArray:
                    violations.Add($"{path}.type");
                    break;
            }
        }
    }
}
