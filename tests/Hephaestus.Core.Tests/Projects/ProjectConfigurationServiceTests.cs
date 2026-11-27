using System.Text;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Tests.Projects;

/// <summary>Story 1.11, FR-2, AD-16: the check asks the gateway for the bytes of the Base Branch's file and its commit id, and Core parses and validates them.</summary>
public class ProjectConfigurationServiceTests
{
    private const string Commit = "3f2a9c1d5e7b8a0c4d6e8f0a1b2c3d4e5f607182";
    private const string Valid = "image: node:22\nvalidation:\n  - npm test\n";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Gateway : ICodeHost
    {
        /// <summary>What each branch of repository 101 holds at its tip.</summary>
        public Dictionary<string, (string CommitId, byte[]? Content)> Branches { get; } = new() { ["main"] = (Commit, Encoding.UTF8.GetBytes(Valid)) };

        public GitHubFileFailure? Fails { get; set; }

        public List<(long RepositoryId, string Branch, string Path, int Maximum)> Reads { get; } = [];

        public List<string> Lookups { get; } = [];

        public Dictionary<string, long> Repositories { get; } = new(StringComparer.OrdinalIgnoreCase) { ["acme/shop"] = 101 };

        public Task<GitHubFileResult> ReadFileAsync(long repositoryId, string branch, string path, int maximumLength, CancellationToken cancellationToken)
        {
            Reads.Add((repositoryId, branch, path, maximumLength));
            if (Fails is { } failure)
            {
                return Task.FromResult(GitHubFileResult.Failed(failure));
            }

            if (!Branches.TryGetValue(branch, out var tip))
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

        public Task<GitHubIssueResult> ReadIssueLabelsAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubIssueResult> ChangeIssueLabelsAsync(long repositoryId, int issueNumber, IReadOnlyList<string> add, IReadOnlyList<string> remove, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubRepositoryResult> FindRepositoryAsync(string fullName, string? branch, CancellationToken cancellationToken)
        {
            Lookups.Add(fullName);
            return Task.FromResult(Repositories.TryGetValue(fullName, out var id)
                ? GitHubRepositoryResult.Found(new GitHubRepository(id, fullName, null))
                : GitHubRepositoryResult.Failed(GitHubRepositoryFailure.NotInstalled));
        }

        public Task<GitHubIdentityResult> ConnectIdentityAsync(long appId, string privateKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubIdentityResult> CheckIdentityAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubUserResult> FindUserAsync(string login, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Methodologies(params string[] names) : IMethodologyCatalog
    {
        public IReadOnlyList<string> ListNames() => names;
    }

    private static (ProjectConfigurationService Service, Gateway Gateway, FakeInstallation Store) Create(params string[] methodologies)
    {
        var gateway = new Gateway();
        var store = new FakeInstallation();
        store.Projects[101] = new Project(101, "acme/shop", "main", DateTimeOffset.UnixEpoch, null);
        var projects = new ProjectService(gateway, store, store);
        return (new ProjectConfigurationService(projects, gateway, store, new Methodologies(methodologies.Length == 0 ? ["default"] : methodologies)), gateway, store);
    }

    private static void Put(Gateway gateway, string content, string branch = "main", string commit = Commit) => gateway.Branches[branch] = (commit, Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task A_valid_file_is_valid_and_the_commit_it_was_read_from_is_named()
    {
        var (service, gateway, _) = Create();

        var check = await service.CheckAsync("acme/shop", Token);

        Assert.Equal(ProjectFailure.None, check.Failure);
        Assert.Equal(ConfigurationStatus.Valid, check.Status);
        Assert.Equal(Commit, check.CommitId);
        Assert.Equal("acme/shop", check.Project!.FullName);
        Assert.Empty(check.Issues);
        Assert.Equal("node:22", check.Configuration!.Image);
        var read = Assert.Single(gateway.Reads);
        Assert.Equal((101L, "main", "hephaestus.yml", ProjectConfigurationFile.MaximumLength), read);
    }

    [Fact]
    public async Task A_Base_Branch_without_the_file_is_reported_missing_with_its_commit()
    {
        var (service, gateway, _) = Create();
        gateway.Branches["main"] = (Commit, null);

        var check = await service.CheckAsync("acme/shop", Token);

        Assert.Equal(ProjectFailure.None, check.Failure);
        Assert.Equal(ConfigurationStatus.Missing, check.Status);
        Assert.Equal(Commit, check.CommitId);
        Assert.Null(check.Configuration);
        Assert.Empty(check.Issues);
    }

    [Fact]
    public async Task A_file_with_several_invalid_settings_has_every_one_named()
    {
        var (service, gateway, _) = Create();
        Put(gateway, "image: Bad Image\nmethodology: nope\ncorrectionRounds: 50\nlanguage: de\nextra: 1\n");

        var check = await service.CheckAsync("acme/shop", Token);

        Assert.Equal(ConfigurationStatus.Invalid, check.Status);
        Assert.Null(check.Configuration);
        Assert.Equal(Commit, check.CommitId);
        Assert.Equal(["image", "methodology", "correctionRounds", "language", "extra", "validation"], check.Issues.Select(i => i.Setting));
    }

    [Fact]
    public async Task Only_the_Base_Branchs_content_is_read_whatever_another_branch_holds()
    {
        var (service, gateway, _) = Create();
        Put(gateway, "image: evil\n", branch: "feature", commit: "ffff");
        Put(gateway, "image: node:20\nvalidation:\n  - make test\n", commit: "aaaa");

        var check = await service.CheckAsync("acme/shop", Token);

        Assert.Equal("node:20", check.Configuration!.Image);
        Assert.Equal("aaaa", check.CommitId);
        Assert.Equal(["main"], gateway.Reads.Select(r => r.Branch));
    }

    [Fact]
    public async Task The_file_is_checked_against_the_Aliases_the_limits_and_the_Methodologies_of_the_installation()
    {
        var (service, gateway, store) = Create("default", "bmad");
        store.Aliases["opus5.5"] = Alias.ForModel("opus5.5", Agent.ClaudeCode, "claude-opus-5-5", DateTimeOffset.UnixEpoch);
        store.Aliases["sonnet5.5"] = Alias.ForModel("sonnet5.5", Agent.ClaudeCode, "claude-sonnet-5-5", DateTimeOffset.UnixEpoch);
        store.Limits = new InstallationLimits(TimeSpan.FromHours(2), null, null);
        Put(gateway, Valid + "primaryAlias: opus5.5\nfallbackAlias: sonnet5.5\nmethodology: bmad\neffort: ghost\nlimits:\n  taskDuration: 5h\n");

        var check = await service.CheckAsync("acme/shop", Token);

        Assert.Equal(
            [
                ("fallbackAlias", MessageIds.ConfigIssueAgentsSame),
                ("effort", MessageIds.ConfigIssueAliasUnknown),
                ("limits.taskDuration", MessageIds.ConfigIssueLimitAboveInstallation),
            ],
            check.Issues.OrderBy(i => i.Line).Select(i => (i.Setting, i.Reason.Id)));
    }

    [Fact]
    public async Task A_file_larger_than_the_bound_is_one_problem_and_is_not_parsed()
    {
        var (service, gateway, _) = Create();
        gateway.Branches["main"] = (Commit, new byte[ProjectConfigurationFile.MaximumLength + 1]);

        var check = await service.CheckAsync("acme/shop", Token);

        Assert.Equal(ConfigurationStatus.Invalid, check.Status);
        Assert.Equal(Commit, check.CommitId);
        var issue = Assert.Single(check.Issues);
        Assert.Equal(("hephaestus.yml", MessageIds.ConfigIssueTooLarge), (issue.Setting, issue.Reason.Id));
    }

    [Fact]
    public async Task A_name_that_is_not_owner_slash_repo_is_refused_and_nothing_is_read()
    {
        var (service, gateway, _) = Create();

        var check = await service.CheckAsync("shop", Token);

        Assert.Equal(ProjectFailure.InvalidName, check.Failure);
        Assert.Empty(gateway.Reads);
    }

    [Fact]
    public async Task A_name_that_is_not_a_registered_Project_is_not_registered_and_nothing_is_read()
    {
        var (service, gateway, _) = Create();

        var check = await service.CheckAsync("acme/other", Token);

        Assert.Equal(ProjectFailure.NotRegistered, check.Failure);
        Assert.Null(check.Project);
        Assert.Empty(gateway.Reads);
    }

    [Fact]
    public async Task A_removed_Project_is_not_checked()
    {
        var (service, gateway, store) = Create();
        store.Projects[101] = store.Projects[101] with { RemovedAt = DateTimeOffset.UnixEpoch.AddDays(1) };

        var check = await service.CheckAsync("acme/shop", Token);

        Assert.Equal(ProjectFailure.NotRegistered, check.Failure);
        Assert.Empty(gateway.Reads);
    }

    [Fact]
    public async Task A_repository_that_was_renamed_is_found_by_the_name_it_has_now_and_read_by_its_id()
    {
        var (service, gateway, store) = Create();
        gateway.Repositories["acme/store"] = 101;

        var check = await service.CheckAsync("acme/store", Token);

        Assert.Equal(ConfigurationStatus.Valid, check.Status);
        Assert.Equal("acme/shop", check.Project!.FullName);
        Assert.Equal(101, Assert.Single(gateway.Reads).RepositoryId);
        Assert.Equal("acme/shop", store.Projects[101].FullName);
    }

    [Theory]
    [InlineData(GitHubFileFailure.NotInstalled, ProjectFailure.NotInstalled)]
    [InlineData(GitHubFileFailure.BranchNotFound, ProjectFailure.BranchNotFound)]
    [InlineData(GitHubFileFailure.NotConnected, ProjectFailure.NotConnected)]
    [InlineData(GitHubFileFailure.KeyUnreadable, ProjectFailure.KeyUnreadable)]
    [InlineData(GitHubFileFailure.Rejected, ProjectFailure.Rejected)]
    [InlineData(GitHubFileFailure.Unreachable, ProjectFailure.Unreachable)]
    public async Task A_read_that_fails_is_the_same_problem_as_when_registering_and_names_the_Project(GitHubFileFailure failure, ProjectFailure expected)
    {
        var (service, gateway, _) = Create();
        gateway.Fails = failure;

        var check = await service.CheckAsync("acme/shop", Token);

        Assert.Equal(expected, check.Failure);
        Assert.Null(check.Status);
        Assert.Equal("main", check.Project!.BaseBranch);
    }

    [Fact]
    public async Task A_check_stores_nothing()
    {
        var (service, gateway, store) = Create();
        Put(gateway, "nonsense: [");
        var before = store.Projects.ToDictionary(p => p.Key, p => p.Value);

        await service.CheckAsync("acme/shop", Token);

        Assert.Equal(before, store.Projects);
        Assert.Empty(store.Aliases);
        Assert.Empty(store.Settings);
    }
}
