using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
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

    /// <summary>The repositories the App is installed on, by name (not case sensitive): the id, the name GitHub spells, and the branches with whether each is protected.</summary>
    public Dictionary<string, (long Id, string FullName, Dictionary<string, bool> Branches)> Repositories { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["acme/shop"] = (101, "acme/shop", new Dictionary<string, bool> { ["main"] = true, ["develop"] = false, ["release/1.0"] = true }),
        ["acme/api"] = (102, "acme/api", new Dictionary<string, bool> { ["main"] = false }),
    };

    /// <summary>When set, every repository lookup fails with it.</summary>
    public GitHubRepositoryFailure? RepositoryLookupFails { get; set; }

    public List<(string FullName, string? Branch)> RepositoryLookups { get; } = [];

    public Task<GitHubRepositoryResult> FindRepositoryAsync(string fullName, string? branch, CancellationToken cancellationToken)
    {
        RepositoryLookups.Add((fullName, branch));
        if (RepositoryLookupFails is { } failure)
        {
            return Task.FromResult(GitHubRepositoryResult.Failed(failure));
        }

        if (!Repositories.TryGetValue(fullName, out var repository))
        {
            return Task.FromResult(GitHubRepositoryResult.Failed(GitHubRepositoryFailure.NotInstalled));
        }

        if (branch is null)
        {
            return Task.FromResult(GitHubRepositoryResult.Found(new GitHubRepository(repository.Id, repository.FullName, Branch: null)));
        }

        return Task.FromResult(repository.Branches.TryGetValue(branch, out var isProtected)
            ? GitHubRepositoryResult.Found(new GitHubRepository(repository.Id, repository.FullName, new GitHubBranch(branch, isProtected)))
            : GitHubRepositoryResult.Failed(GitHubRepositoryFailure.BranchNotFound));
    }

    /// <summary>
    /// The branches of a repository by its id and what each holds at its tip: the commit id and the bytes of <c>hephaestus.yml</c>, or null when the branch has none.
    /// A branch that is not here does not exist.
    /// </summary>
    public Dictionary<(long RepositoryId, string Branch), (string CommitId, byte[]? Content)> Files { get; } = [];

    /// <summary>When set, every file read fails with it.</summary>
    public GitHubFileFailure? FileReadFails { get; set; }

    public List<(long RepositoryId, string Branch, string Path)> FileReads { get; } = [];

    public Task<GitHubFileResult> ReadFileAsync(long repositoryId, string branch, string path, int maximumLength, CancellationToken cancellationToken)
    {
        FileReads.Add((repositoryId, branch, path));
        if (FileReadFails is { } failure)
        {
            return Task.FromResult(GitHubFileResult.Failed(failure));
        }

        if (!Files.TryGetValue((repositoryId, branch), out var tip))
        {
            return Task.FromResult(GitHubFileResult.Failed(GitHubFileFailure.BranchNotFound));
        }

        return Task.FromResult(tip.Content switch
        {
            null => GitHubFileResult.Failed(GitHubFileFailure.NotFound, tip.CommitId),
            { Length: var length } when length > maximumLength => GitHubFileResult.Failed(GitHubFileFailure.TooLarge, tip.CommitId),
            var content => GitHubFileResult.Read(new GitHubFile(content, tip.CommitId)),
        });
    }

    /// <summary>The labels of the issues, by repository id and issue number; an issue that is not here does not exist.</summary>
    public Dictionary<(long RepositoryId, int IssueNumber), List<string>> IssueLabels { get; } = [];

    /// <summary>When set, every read or change of labels fails with it.</summary>
    public GitHubIssueFailure? IssueFails { get; set; }

    public List<(long RepositoryId, int IssueNumber, string[] Add, string[] Remove, string Key)> LabelChanges { get; } = [];

    public Task<GitHubIssueResult> ReadIssueLabelsAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken)
    {
        if (IssueFails is { } failure)
        {
            return Task.FromResult(GitHubIssueResult.Failed(failure));
        }

        return Task.FromResult(IssueLabels.TryGetValue((repositoryId, issueNumber), out var labels)
            ? GitHubIssueResult.Labelled([.. labels])
            : GitHubIssueResult.Failed(GitHubIssueFailure.IssueNotFound));
    }

    public Task<GitHubIssueResult> ChangeIssueLabelsAsync(
        long repositoryId,
        int issueNumber,
        IReadOnlyList<string> add,
        IReadOnlyList<string> remove,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (IssueFails is { } failure)
        {
            return Task.FromResult(GitHubIssueResult.Failed(failure));
        }

        if (!IssueLabels.TryGetValue((repositoryId, issueNumber), out var labels))
        {
            return Task.FromResult(GitHubIssueResult.Failed(GitHubIssueFailure.IssueNotFound));
        }

        LabelChanges.Add((repositoryId, issueNumber, [.. add], [.. remove], idempotencyKey));
        labels.RemoveAll(label => remove.Contains(label, StringComparer.OrdinalIgnoreCase));
        labels.AddRange(add.Where(label => !labels.Contains(label, StringComparer.OrdinalIgnoreCase)));
        return Task.FromResult(GitHubIssueResult.Labelled([.. labels]));
    }
}
