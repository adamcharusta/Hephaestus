using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Tests.Projects;

/// <summary>Story 1.10, FR-1, AD-19: a Project is a repository the App is installed on, identified by GitHub's id, with a Base Branch that exists.</summary>
public class ProjectServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Gateway : ICodeHost
    {
        public Dictionary<string, (long Id, string FullName, Dictionary<string, bool> Branches)> Repositories { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["acme/shop"] = (101, "acme/shop", new Dictionary<string, bool> { ["main"] = true, ["develop"] = false }),
        };

        public GitHubRepositoryFailure? Fails { get; set; }

        public List<(string Name, string? Branch)> Lookups { get; } = [];

        public Task<GitHubRepositoryResult> FindRepositoryAsync(string fullName, string? branch, CancellationToken cancellationToken)
        {
            Lookups.Add((fullName, branch));
            if (Fails is { } failure)
            {
                return Task.FromResult(GitHubRepositoryResult.Failed(failure));
            }

            if (!Repositories.TryGetValue(fullName, out var repository))
            {
                return Task.FromResult(GitHubRepositoryResult.Failed(GitHubRepositoryFailure.NotInstalled));
            }

            if (branch is null)
            {
                return Task.FromResult(GitHubRepositoryResult.Found(new GitHubRepository(repository.Id, repository.FullName, null)));
            }

            return Task.FromResult(repository.Branches.TryGetValue(branch, out var isProtected)
                ? GitHubRepositoryResult.Found(new GitHubRepository(repository.Id, repository.FullName, new GitHubBranch(branch, isProtected)))
                : GitHubRepositoryResult.Failed(GitHubRepositoryFailure.BranchNotFound));
        }

        public Task<GitHubIdentityResult> ConnectIdentityAsync(long appId, string privateKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubIdentityResult> CheckIdentityAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubUserResult> FindUserAsync(string login, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubFileResult> ReadFileAsync(long repositoryId, string branch, string path, int maximumLength, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubIssueResult> ReadIssueLabelsAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubIssueResult> ChangeIssueLabelsAsync(long repositoryId, int issueNumber, IReadOnlyList<string> add, IReadOnlyList<string> remove, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static (ProjectService Service, Gateway Gateway, FakeInstallation Store) Create()
    {
        var gateway = new Gateway();
        var store = new FakeInstallation();
        return (new ProjectService(gateway, store, store), gateway, store);
    }

    [Fact]
    public async Task A_repository_with_the_App_and_a_protected_Base_Branch_is_registered_by_its_id_and_reported_protected()
    {
        var (service, _, store) = Create();

        var result = await service.AddAsync("acme/shop", "main", Token);

        Assert.Equal(ProjectFailure.None, result.Failure);
        Assert.Equal(ProjectChange.Registered, result.Change);
        Assert.True(result.BaseBranchProtected);
        var stored = Assert.Single(store.Projects).Value;
        Assert.Equal((101L, "acme/shop", "main"), (stored.RepositoryId, stored.FullName, stored.BaseBranch));
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task An_unprotected_Base_Branch_is_reported_so_and_is_still_registered()
    {
        var (service, _, store) = Create();

        var result = await service.AddAsync("acme/shop", "develop", Token);

        Assert.Equal(ProjectFailure.None, result.Failure);
        Assert.False(result.BaseBranchProtected);
        Assert.Single(store.Projects);
    }

    [Fact]
    public async Task The_name_and_branch_stored_are_the_ones_GitHub_spells_not_the_ones_typed()
    {
        var (service, _, store) = Create();

        var result = await service.AddAsync("ACME/Shop", "main", Token);

        Assert.Equal("acme/shop", result.Project!.FullName);
        Assert.Equal("acme/shop", store.Projects[101].FullName);
    }

    [Fact]
    public async Task A_repository_the_App_is_not_installed_on_fails_as_such_and_stores_nothing()
    {
        var (service, _, store) = Create();

        var result = await service.AddAsync("acme/other", "main", Token);

        Assert.Equal(ProjectFailure.NotInstalled, result.Failure);
        Assert.Null(result.Project);
        Assert.Empty(store.Projects);
    }

    [Fact]
    public async Task A_Base_Branch_that_does_not_exist_fails_as_such_and_stores_nothing()
    {
        var (service, _, store) = Create();

        var result = await service.AddAsync("acme/shop", "nope", Token);

        Assert.Equal(ProjectFailure.BranchNotFound, result.Failure);
        Assert.Empty(store.Projects);
    }

    [Theory]
    [InlineData("shop", "main", ProjectFailure.InvalidName)]
    [InlineData("acme/shop/x", "main", ProjectFailure.InvalidName)]
    [InlineData("acme/shop", "a..b", ProjectFailure.InvalidBranch)]
    [InlineData("acme/shop", "a b", ProjectFailure.InvalidBranch)]
    [InlineData("acme/shop", null, ProjectFailure.InvalidBranch)]
    [InlineData("acme/shop", "", ProjectFailure.InvalidBranch)]
    public async Task A_name_or_branch_of_the_wrong_shape_is_refused_before_GitHub_is_asked(string name, string? branch, ProjectFailure failure)
    {
        var (service, gateway, store) = Create();

        var result = await service.AddAsync(name, branch, Token);

        Assert.Equal(failure, result.Failure);
        Assert.Empty(gateway.Lookups);
        Assert.Empty(store.Projects);
    }

    [Theory]
    [InlineData(GitHubRepositoryFailure.NotConnected, ProjectFailure.NotConnected)]
    [InlineData(GitHubRepositoryFailure.KeyUnreadable, ProjectFailure.KeyUnreadable)]
    [InlineData(GitHubRepositoryFailure.Rejected, ProjectFailure.Rejected)]
    [InlineData(GitHubRepositoryFailure.Unreachable, ProjectFailure.Unreachable)]
    public async Task A_failure_of_GitHub_is_reported_as_what_it_was_and_stores_nothing(GitHubRepositoryFailure github, ProjectFailure expected)
    {
        var (service, gateway, store) = Create();
        gateway.Fails = github;

        var result = await service.AddAsync("acme/shop", "main", Token);

        Assert.Equal(expected, result.Failure);
        Assert.Empty(store.Projects);
    }

    [Fact]
    public async Task Registering_a_registered_Project_again_changes_nothing()
    {
        var (service, _, store) = Create();
        await service.AddAsync("acme/shop", "main", Token);

        var again = await service.AddAsync("acme/shop", "main", Token);

        Assert.Equal(ProjectChange.Unchanged, again.Change);
        Assert.Single(store.Projects);
    }

    [Fact]
    public async Task Registering_with_another_Base_Branch_changes_the_Base_Branch_and_says_updated()
    {
        var (service, _, store) = Create();
        await service.AddAsync("acme/shop", "main", Token);

        var again = await service.AddAsync("acme/shop", "develop", Token);

        Assert.Equal(ProjectChange.Updated, again.Change);
        Assert.Equal("develop", store.Projects[101].BaseBranch);
    }

    [Fact]
    public async Task A_renamed_repository_is_the_same_Project_under_its_new_name()
    {
        var (service, gateway, store) = Create();
        await service.AddAsync("acme/shop", "main", Token);
        gateway.Repositories.Remove("acme/shop");
        gateway.Repositories["acme/store"] = (101, "acme/store", new Dictionary<string, bool> { ["main"] = true });

        var again = await service.AddAsync("acme/store", "main", Token);

        Assert.Equal(ProjectChange.Updated, again.Change);
        var stored = Assert.Single(store.Projects).Value;
        Assert.Equal((101L, "acme/store"), (stored.RepositoryId, stored.FullName));
    }

    [Fact]
    public async Task Removing_a_Project_retires_it_and_keeps_the_record()
    {
        var (service, gateway, store) = Create();
        await service.AddAsync("acme/shop", "main", Token);
        gateway.Lookups.Clear();

        var result = await service.RemoveAsync("ACME/shop", Token);

        Assert.Equal(ProjectFailure.None, result.Failure);
        Assert.Equal(101, result.Project!.RepositoryId);
        Assert.Empty(gateway.Lookups);
        var kept = Assert.Single(store.Projects).Value;
        Assert.False(kept.IsActive);
        Assert.Empty(await store.ListProjectsAsync(includeRemoved: false, Token));
        Assert.Single(await store.ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task A_removed_Project_can_be_registered_again_from_its_record()
    {
        var (service, _, store) = Create();
        await service.AddAsync("acme/shop", "main", Token);
        await service.RemoveAsync("acme/shop", Token);

        var again = await service.AddAsync("acme/shop", "main", Token);

        Assert.Equal(ProjectChange.Registered, again.Change);
        Assert.True(store.Projects[101].IsActive);
        Assert.Single(store.Projects);
    }

    [Fact]
    public async Task A_renamed_repository_is_removed_under_the_name_it_has_now()
    {
        var (service, gateway, store) = Create();
        await service.AddAsync("acme/shop", "main", Token);
        gateway.Repositories.Remove("acme/shop");
        gateway.Repositories["acme/store"] = (101, "acme/store", new Dictionary<string, bool> { ["main"] = true });

        var result = await service.RemoveAsync("acme/store", Token);

        Assert.Equal(ProjectFailure.None, result.Failure);
        Assert.False(store.Projects[101].IsActive);
        Assert.Equal([("acme/store", (string?)null)], gateway.Lookups.Skip(1));
    }

    [Fact]
    public async Task When_a_stale_name_beside_the_current_one_is_stored_GitHub_says_which_Project_the_name_is_now()
    {
        var (service, gateway, store) = Create();
        await service.AddAsync("acme/shop", "main", Token);
        gateway.Repositories.Remove("acme/shop");
        gateway.Repositories["acme/store"] = (101, "acme/store", new Dictionary<string, bool> { ["main"] = true });
        gateway.Repositories["acme/shop"] = (202, "acme/shop", new Dictionary<string, bool> { ["main"] = true });
        await service.AddAsync("acme/shop", "main", Token);
        gateway.Lookups.Clear();

        var result = await service.RemoveAsync("acme/shop", Token);

        Assert.Equal(ProjectFailure.None, result.Failure);
        Assert.Equal(202, result.Project!.RepositoryId);
        Assert.False(store.Projects[202].IsActive);
        Assert.True(store.Projects[101].IsActive);
        Assert.Equal([("acme/shop", (string?)null)], gateway.Lookups);
    }

    [Fact]
    public async Task A_name_that_is_not_a_registered_Project_is_not_found()
    {
        var (service, gateway, _) = Create();
        gateway.Repositories["acme/api"] = (102, "acme/api", new Dictionary<string, bool> { ["main"] = true });
        await service.AddAsync("acme/shop", "main", Token);

        Assert.Equal(ProjectFailure.NotRegistered, (await service.RemoveAsync("acme/api", Token)).Failure);
        Assert.Equal(ProjectFailure.NotRegistered, (await service.RemoveAsync("acme/unknown", Token)).Failure);
    }

    [Fact]
    public async Task With_no_Project_registered_nothing_is_looked_up_and_the_name_is_not_found()
    {
        var (service, gateway, _) = Create();

        var result = await service.RemoveAsync("acme/shop", Token);

        Assert.Equal(ProjectFailure.NotRegistered, result.Failure);
        Assert.Empty(gateway.Lookups);
    }

    [Fact]
    public async Task A_Project_that_was_already_removed_is_not_a_registered_Project()
    {
        var (service, _, _) = Create();
        await service.AddAsync("acme/shop", "main", Token);
        await service.RemoveAsync("acme/shop", Token);

        Assert.Equal(ProjectFailure.NotRegistered, (await service.RemoveAsync("acme/shop", Token)).Failure);
    }

    [Fact]
    public async Task A_name_of_the_wrong_shape_is_refused_when_removing_too()
    {
        var (service, gateway, _) = Create();

        Assert.Equal(ProjectFailure.InvalidName, (await service.RemoveAsync("shop", Token)).Failure);
        Assert.Empty(gateway.Lookups);
    }

    [Theory]
    [InlineData(GitHubRepositoryFailure.NotConnected, ProjectFailure.NotConnected)]
    [InlineData(GitHubRepositoryFailure.Unreachable, ProjectFailure.Unreachable)]
    [InlineData(GitHubRepositoryFailure.Rejected, ProjectFailure.Rejected)]
    public async Task When_the_stored_names_do_not_match_and_GitHub_cannot_say_the_failure_is_reported(GitHubRepositoryFailure github, ProjectFailure expected)
    {
        var (service, gateway, store) = Create();
        await service.AddAsync("acme/shop", "main", Token);
        gateway.Fails = github;

        var result = await service.RemoveAsync("acme/store", Token);

        Assert.Equal(expected, result.Failure);
        Assert.True(store.Projects[101].IsActive);
    }

    [Fact]
    public async Task A_removal_that_finds_the_Project_already_gone_is_not_found()
    {
        var (service, _, store) = Create();
        await service.AddAsync("acme/shop", "main", Token);
        store.RemovalFindsTheProjectGone = true;

        var result = await service.RemoveAsync("acme/shop", Token);

        Assert.Equal(ProjectFailure.NotRegistered, result.Failure);
    }
}
