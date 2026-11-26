namespace Hephaestus.Core.GitHub;

/// <summary>
/// The permissions of Hephaestus's own GitHub App (AD-19, NFR-6): contents, issues, and pull requests read and write; metadata,
/// checks, and statuses read; never <c>workflows</c> or administration. Octokit never reaches Core, so the gateway hands over the
/// levels GitHub reports, by name, and this decides what they mean.
/// </summary>
public static class GitHubPermissionPolicy
{
    public const string Contents = "contents";
    public const string Issues = "issues";
    public const string PullRequests = "pull-requests";
    public const string Metadata = "metadata";
    public const string Checks = "checks";
    public const string Statuses = "statuses";
    public const string Workflows = "workflows";
    public const string Administration = "administration";
    public const string OrganizationAdministration = "organization-administration";

    /// <summary>What the App needs, in the order the report lists it.</summary>
    public static IReadOnlyList<(string Name, GitHubPermissionLevel Level)> Required { get; } =
    [
        (Contents, GitHubPermissionLevel.Write),
        (Issues, GitHubPermissionLevel.Write),
        (PullRequests, GitHubPermissionLevel.Write),
        (Metadata, GitHubPermissionLevel.Read),
        (Checks, GitHubPermissionLevel.Read),
        (Statuses, GitHubPermissionLevel.Read),
    ];

    /// <summary>What the App must not have, at any level.</summary>
    public static IReadOnlyList<string> Forbidden { get; } = [Workflows, Administration, OrganizationAdministration];

    /// <summary>
    /// Judges the permissions an App was granted. A permission that is absent from <paramref name="granted"/> is not granted; a
    /// level above the one needed is enough, and one below it is missing.
    /// </summary>
    public static (IReadOnlyList<GitHubPermissionCheck> Permissions, IReadOnlyList<GitHubPermissionWarning> Warnings) Evaluate(IReadOnlyDictionary<string, GitHubPermissionLevel> granted)
    {
        GitHubPermissionLevel LevelOf(string name) => granted.GetValueOrDefault(name, GitHubPermissionLevel.None);

        var permissions = Required
            .Select(r => new GitHubPermissionCheck(r.Name, r.Level, LevelOf(r.Name), LevelOf(r.Name) >= r.Level))
            .ToList();
        var warnings = Forbidden
            .Where(name => LevelOf(name) != GitHubPermissionLevel.None)
            .Select(name => new GitHubPermissionWarning(name, LevelOf(name)))
            .ToList();
        return (permissions, warnings);
    }
}
