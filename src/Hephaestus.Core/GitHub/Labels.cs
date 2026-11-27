namespace Hephaestus.Core.GitHub;

/// <summary>The label conventions on GitHub (AR-33); part of the versioned public surface and never translated.</summary>
public static class Labels
{
    /// <summary>The Marker Label: applied by a Trusted User, it opts an issue in.</summary>
    public const string Marker = "hephaestus";

    /// <summary>GitHub treats label names as case-insensitive, so a label is the Marker Label whatever its case.</summary>
    public static bool IsMarker(string? label) => string.Equals(label, Marker, StringComparison.OrdinalIgnoreCase);
}
