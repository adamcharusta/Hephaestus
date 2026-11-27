using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Projects;

/// <summary>The resource limits a Project asks for, each within the installation's (FR-5, NFR-5); a limit the file does not name is null and is the installation's.</summary>
public sealed record ProjectLimits(TimeSpan? TaskDuration, int? CpuMillis, long? MemoryBytes)
{
    public static ProjectLimits None { get; } = new(null, null, null);
}

/// <summary>
/// The settings of a valid <c>hephaestus.yml</c> (FR-2, AD-16), with the defaults that do not depend on the installation applied. A setting the
/// file leaves out and that has no default is null: the Aliases are then the Task's own labels', the limits the installation's, and the language its.
/// </summary>
/// <param name="PrimaryAlias">The Alias of the default Primary Agent with its Model, or null.</param>
/// <param name="FallbackAlias">The Alias of the default Fallback Agent with its Model, or null.</param>
/// <param name="Effort">The Alias of the default Effort, or null.</param>
/// <param name="Methodology">The Methodology's name; <c>default</c> when the file names none.</param>
/// <param name="Validation">The Validation commands, at least one, each run through <c>bash</c>.</param>
/// <param name="CorrectionRounds">The bound of the Correction Loop: the rounds at most; two when the file names none (FR-34).</param>
/// <param name="Image">The image the Project's Tasks run in.</param>
/// <param name="AllowedEgressHosts">The host names a Task may reach beyond what its Agent needs, in lower case; none means any public host (AD-7).</param>
/// <param name="Language">The language of the text written on the Project's issues and pull requests; null is the installation's (AD-21).</param>
public sealed record ProjectConfiguration(
    string? PrimaryAlias,
    string? FallbackAlias,
    string? Effort,
    string Methodology,
    IReadOnlyList<string> Validation,
    int CorrectionRounds,
    string Image,
    ProjectLimits Limits,
    IReadOnlyList<string> AllowedEgressHosts,
    Language? Language);

/// <summary>The one file, its keys, and its bounds. A key is never translated.</summary>
public static class ProjectConfigurationFile
{
    public const string Path = "hephaestus.yml";

    /// <summary>The longest file that is read; a larger one is an error, so that a file cannot ask for more memory than a person would write.</summary>
    public const int MaximumLength = 64 * 1024;

    public const string DefaultMethodology = "default";
    public const int DefaultCorrectionRounds = 2;
    public const int MaximumCorrectionRounds = 10;
    public const int MaximumValidationCommands = 20;
    public const int MaximumCommandLength = 2000;
    public const int MaximumEgressHosts = 100;
    public const int MaximumNesting = 8;

    public const string PrimaryAliasKey = "primaryAlias";
    public const string FallbackAliasKey = "fallbackAlias";
    public const string EffortKey = "effort";
    public const string MethodologyKey = "methodology";
    public const string ValidationKey = "validation";
    public const string CorrectionRoundsKey = "correctionRounds";
    public const string ImageKey = "image";
    public const string LimitsKey = "limits";
    public const string TaskDurationKey = "taskDuration";
    public const string CpusKey = "cpus";
    public const string MemoryKey = "memory";
    public const string AllowedEgressHostsKey = "allowedEgressHosts";
    public const string LanguageKey = "language";

    /// <summary>The top-level keys, in the order the reference describes them.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        PrimaryAliasKey, FallbackAliasKey, EffortKey, MethodologyKey, ValidationKey, CorrectionRoundsKey, ImageKey, LimitsKey, AllowedEgressHostsKey, LanguageKey,
    ];

    /// <summary>The keys of <c>limits</c>.</summary>
    public static IReadOnlyList<string> LimitKeys { get; } = [TaskDurationKey, CpusKey, MemoryKey];
}
