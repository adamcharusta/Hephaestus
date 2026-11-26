using System.Globalization;

namespace Hephaestus.Core.Protection;

/// <summary>
/// A protected path is a glob over repository paths that an Agent's change may never touch (AD-19). The list is Installation
/// Configuration; this is the shape of one entry and the list a new installation starts with.
/// </summary>
public static class ProtectedPath
{
    public const int MaximumLength = 200;

    /// <summary>The list of a new installation: the repository's own automation and Hephaestus's own configuration.</summary>
    public static IReadOnlyList<string> Defaults { get; } = [".github/**", "hephaestus.yml"];

    /// <summary>
    /// A relative path with <c>/</c> between non-empty segments, none of them <c>.</c> or <c>..</c>, and no backslash, control
    /// character, or space at either end, so a glob can never name a path outside the repository or be read in two ways.
    /// </summary>
    public static bool IsValid(string? glob)
    {
        if (glob is not { Length: > 0 and <= MaximumLength } || glob.Any(c => char.IsControl(c) || c == '\\' || char.GetUnicodeCategory(c) == UnicodeCategory.Format) || char.IsWhiteSpace(glob[0]) || char.IsWhiteSpace(glob[^1]))
        {
            return false;
        }

        return glob.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));
    }
}
