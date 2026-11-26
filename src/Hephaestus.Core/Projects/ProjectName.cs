using System.Text.RegularExpressions;

namespace Hephaestus.Core.Projects;

/// <summary>The shapes of a repository's name and of a Base Branch, checked before GitHub is asked so that nothing odd is sent in an address.</summary>
public static partial class ProjectName
{
    public const int MaximumOwnerLength = 39;
    public const int MaximumRepositoryLength = 100;
    public const int MaximumBranchLength = 255;

    /// <summary>
    /// Splits <c>owner/repo</c>: an owner is a GitHub login (letters, digits, and hyphens inside, and underscores, which an Enterprise Managed User's login has) and a repository is letters, digits,
    /// <c>.</c>, <c>_</c>, and <c>-</c>, but never <c>.</c> or <c>..</c> alone.
    /// </summary>
    public static bool TryParse(string? fullName, out string owner, out string repository)
    {
        owner = repository = string.Empty;
        if (fullName is null)
        {
            return false;
        }

        var parts = fullName.Split('/');
        if (parts.Length != 2 || !OwnerShape().IsMatch(parts[0]) || parts[0].Length > MaximumOwnerLength || parts[1].Length > MaximumRepositoryLength || !RepositoryShape().IsMatch(parts[1]) || parts[1] is "." or ".." || parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        (owner, repository) = (parts[0], parts[1]);
        return true;
    }

    public static bool IsValid(string? fullName) => TryParse(fullName, out _, out _);

    public static bool SameName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A branch name git accepts (<c>git check-ref-format</c>) and that no command line takes for an option: no control character,
    /// space, or <c>~ ^ : ? * [ \</c>; no <c>..</c>, <c>@{</c>, or <c>//</c>; no leading <c>-</c> or <c>/</c>; no trailing <c>/</c> or <c>.</c>; no segment that starts
    /// with <c>.</c> or ends with <c>.lock</c>; and not <c>@</c> alone.
    /// </summary>
    public static bool IsValidBranch(string? branch)
    {
        if (branch is not { Length: > 0 and <= MaximumBranchLength } || branch is "@" || branch[0] is '-' or '/' || branch[^1] is '/' or '.')
        {
            return false;
        }

        if (branch.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\' || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
        {
            return false;
        }

        return !branch.Contains("..", StringComparison.Ordinal)
            && !branch.Contains("@{", StringComparison.Ordinal)
            && branch.Split('/').All(segment => segment.Length > 0 && segment[0] != '.' && !segment.EndsWith(".lock", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9_-]*[A-Za-z0-9])?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OwnerShape();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RepositoryShape();
}
