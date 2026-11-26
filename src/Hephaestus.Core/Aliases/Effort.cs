namespace Hephaestus.Core.Aliases;

/// <summary>
/// The reasoning effort requested from a Model (the Glossary's Effort): Core's closed set (AD-10). Each Agent's adapter maps a value
/// to its vendor's flag or declares it unsupported; a value outside this set can never reach an adapter.
/// </summary>
public enum Effort
{
    Low,
    Medium,
    High,
    ExtraHigh,
    Max,
}

public static class Efforts
{
    /// <summary>The name a command, a document, or a configuration file carries; never translated.</summary>
    public static string Tag(this Effort effort) => effort switch
    {
        Effort.Low => "low",
        Effort.Medium => "medium",
        Effort.High => "high",
        Effort.ExtraHigh => "xhigh",
        Effort.Max => "max",
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null),
    };

    public static bool TryParse(string? tag, out Effort effort)
    {
        var normalized = tag?.Trim().ToLowerInvariant();
        foreach (var candidate in Enum.GetValues<Effort>())
        {
            if (candidate.Tag() == normalized)
            {
                effort = candidate;
                return true;
            }
        }

        effort = default;
        return false;
    }

    /// <summary>The tags in ascending order of effort.</summary>
    public static IReadOnlyList<string> Tags { get; } = [.. Enum.GetValues<Effort>().Select(Tag)];
}
