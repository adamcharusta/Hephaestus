using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;

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
}
