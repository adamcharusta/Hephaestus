using System.Text.Json.Serialization;

namespace Hephaestus.Core.Validation;

/// <summary>The content of <c>validation.json</c>: keys and statuses are never translated.</summary>
/// <param name="Outcome">One of <see cref="ValidationOutcomes.Tag"/>.</param>
internal sealed record ValidationDocument(string Outcome, IReadOnlyList<ValidationCommandDocument> Commands)
{
    public static ValidationDocument From(ValidationRun run) =>
        new(
            run.Outcome?.Tag() ?? throw new InvalidOperationException("A command is still to run, so Validation has no results yet."),
            [.. run.Results.Select(ValidationCommandDocument.From)]);
}

/// <param name="Output">The container run of the command, which keys its one output file; null when the command was not run.</param>
internal sealed record ValidationCommandDocument(int Index, string Command, string Status, int? ExitCode, long? DurationMilliseconds, Guid? Output)
{
    public static ValidationCommandDocument From(ValidationCommandResult result) =>
        new(result.Index, result.Command, result.Status.Tag(), result.ExitCode, result.Duration is { } duration ? (long)duration.TotalMilliseconds : null, result.StepRunId);
}

/// <summary>Source-generated JSON of <c>validation.json</c>: camelCase keys, indented so that a person can read what an Agent was given, and the same bytes for the same results.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ValidationDocument))]
internal sealed partial class ValidationJson : JsonSerializerContext;
