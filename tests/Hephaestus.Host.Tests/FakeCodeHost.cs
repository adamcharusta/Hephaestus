using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Trust;

namespace Hephaestus.Host.Tests;

/// <summary>A gateway that answers what a test says and remembers what it was asked, so the API and the command line are tested without GitHub.</summary>
internal sealed class FakeCodeHost : ICodeHost
{
    public static readonly GitHubIdentity Identity = new(
        123456,
        "hephaestus-test",
        "Hephaestus Test",
        "adamcharusta",
        [
            new GitHubInstallation(10, "acme", "Organization", "all", false, ["acme/api", "acme/web"]),
            new GitHubInstallation(11, "adamcharusta", "User", "selected", false, []),
            new GitHubInstallation(12, "old-org", "Organization", "all", true, []),
        ],
        [
            new GitHubPermissionCheck("contents", GitHubPermissionLevel.Write, GitHubPermissionLevel.Write, true),
            new GitHubPermissionCheck("issues", GitHubPermissionLevel.Write, GitHubPermissionLevel.Write, true),
            new GitHubPermissionCheck("pull-requests", GitHubPermissionLevel.Write, GitHubPermissionLevel.Read, false),
            new GitHubPermissionCheck("metadata", GitHubPermissionLevel.Read, GitHubPermissionLevel.Read, true),
            new GitHubPermissionCheck("checks", GitHubPermissionLevel.Read, GitHubPermissionLevel.None, false),
            new GitHubPermissionCheck("statuses", GitHubPermissionLevel.Read, GitHubPermissionLevel.Read, true),
        ],
        [new GitHubPermissionWarning("workflows", GitHubPermissionLevel.Write)]);

    public GitHubIdentityResult Answer { get; set; } = GitHubIdentityResult.Succeeded(Identity);

    public List<(long AppId, string PrivateKey)> Connects { get; } = [];

    public int Checks { get; private set; }

    public Task<GitHubIdentityResult> ConnectIdentityAsync(long appId, string privateKey, CancellationToken cancellationToken)
    {
        Connects.Add((appId, privateKey));
        return Task.FromResult(Answer);
    }

    public Task<GitHubIdentityResult> CheckIdentityAsync(CancellationToken cancellationToken)
    {
        Checks++;
        return Task.FromResult(Answer);
    }

    /// <summary>What GitHub says of a login, ignoring case; a login that is not here is not found.</summary>
    public Dictionary<string, GitHubUser> Users { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alice"] = new GitHubUser(583231, "Alice", IsHephaestus: false),
        ["bob"] = new GitHubUser(1001, "bob", IsHephaestus: false),
        ["hephaestus-test[bot]"] = new GitHubUser(99999, "hephaestus-test[bot]", IsHephaestus: true),
    };

    /// <summary>When set, every lookup fails with it.</summary>
    public GitHubUserFailure? LookupFails { get; set; }

    public List<string> Lookups { get; } = [];

    public Task<GitHubUserResult> FindUserAsync(string login, CancellationToken cancellationToken)
    {
        Lookups.Add(login);
        if (LookupFails is { } failure)
        {
            return Task.FromResult(GitHubUserResult.Failed(failure));
        }

        return Task.FromResult(Users.TryGetValue(login, out var user) ? GitHubUserResult.Found(user) : GitHubUserResult.Failed(GitHubUserFailure.NotFound));
    }
}
