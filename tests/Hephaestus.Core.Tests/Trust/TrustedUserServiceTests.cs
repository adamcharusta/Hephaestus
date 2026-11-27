using Hephaestus.Core.Aliases;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Tests.Trust;

/// <summary>Story 1.8, AD-15: a Trusted User is a numeric id found through the gateway; the login is display only, and Hephaestus's own identity is refused.</summary>
public class TrustedUserServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Fixture : ICodeHost, IReadStore, IInstallationConfiguration
    {
        public Fixture()
        {
            Service = new TrustedUserService(this, this, this);
        }

        public TrustedUserService Service { get; }

        public Dictionary<string, GitHubUser> Users { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["alice"] = new GitHubUser(1, "Alice", IsHephaestus: false),
            ["alice-renamed"] = new GitHubUser(1, "alice-renamed", IsHephaestus: false),
            ["bob"] = new GitHubUser(2, "bob", IsHephaestus: false),
            ["hephaestus[bot]"] = new GitHubUser(3, "hephaestus[bot]", IsHephaestus: true),
        };

        public GitHubUserFailure? LookupFails { get; set; }

        public List<string> Lookups { get; } = [];

        public List<TrustedUser> Stored { get; } = [];

        public Task<GitHubUserResult> FindUserAsync(string login, CancellationToken cancellationToken)
        {
            Lookups.Add(login);
            return Task.FromResult(LookupFails is { } failure
                ? GitHubUserResult.Failed(failure)
                : Users.TryGetValue(login, out var user) ? GitHubUserResult.Found(user) : GitHubUserResult.Failed(GitHubUserFailure.NotFound));
        }

        public Task<IReadOnlyList<TrustedUser>> ListTrustedUsersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrustedUser>>([.. Stored]);

        public Task<TrustedUserWrite> AddTrustedUserAsync(long userId, string login, CancellationToken cancellationToken)
        {
            var existing = Stored.FindIndex(u => u.UserId == userId);
            var user = new TrustedUser(userId, login, DateTimeOffset.UnixEpoch);
            if (existing >= 0)
            {
                Stored[existing] = user;
            }
            else
            {
                Stored.Add(user);
            }

            return Task.FromResult(new TrustedUserWrite(user, existing < 0));
        }

        public Task<bool> RemoveTrustedUserAsync(long userId, CancellationToken cancellationToken) => Task.FromResult(Stored.RemoveAll(u => u.UserId == userId) > 0);

        public Task<GitHubIdentityResult> ConnectIdentityAsync(long appId, string privateKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubIdentityResult> CheckIdentityAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GitHubRepositoryResult> FindRepositoryAsync(string fullName, string? branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubFileResult> ReadFileAsync(long repositoryId, string branch, string path, int maximumLength, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubIssueResult> ReadIssueLabelsAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubIssueResult> ChangeIssueLabelsAsync(long repositoryId, int issueNumber, IReadOnlyList<string> add, IReadOnlyList<string> remove, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubCommentResult> FindIssueCommentAsync(long repositoryId, int issueNumber, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubCommentResult> PostIssueCommentAsync(long repositoryId, int issueNumber, string text, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Project>> ListProjectsAsync(bool includeRemoved, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProjectWrite> RegisterProjectAsync(long repositoryId, string fullName, string baseBranch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Project?> RemoveProjectAsync(long repositoryId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetGitHubAppIdAsync(long appId, string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetGitHubAppSlugAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Hephaestus.Core.Tasks.TaskRecord?> FindTaskAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Hephaestus.Core.Tasks.IssueSnapshot?> FindIssueSnapshotAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Hephaestus.Core.Effects.PendingEffect>> ListUnfinishedEffectsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Hephaestus.Core.Effects.JournalEntry>> ListJournalAsync(Guid? taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Hephaestus.Core.Escalations.Escalation?> FindEscalationAsync(Guid escalationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Hephaestus.Core.Escalations.Escalation>> ListEscalationsAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<AliasWrite> AddAliasAsync(Alias alias, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Alias?> RemoveAliasAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallationLimits> SetLimitsAsync(LimitsChange change, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> AddProtectedPathAsync(string glob, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> RemoveProtectedPathAsync(string glob, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetSettingAsync(string name, string value, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Adding_a_login_stores_the_id_GitHub_names_with_the_login_GitHub_spells()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.AddAsync("alice", Token);

        Assert.Equal(TrustedUserFailure.None, result.Failure);
        Assert.True(result.Changed);
        Assert.Equal((1L, "Alice"), (result.User!.UserId, result.User.Login));
        Assert.Equal([1L], fixture.Stored.Select(u => u.UserId));
        Assert.Equal(["alice"], fixture.Lookups);
    }

    [Fact]
    public async Task Adding_the_same_user_again_stores_nothing_new_and_says_so()
    {
        var fixture = new Fixture();
        await fixture.Service.AddAsync("alice", Token);

        var again = await fixture.Service.AddAsync("ALICE", Token);

        Assert.Equal(TrustedUserFailure.None, again.Failure);
        Assert.False(again.Changed);
        Assert.Single(fixture.Stored);
    }

    [Fact]
    public async Task A_user_who_changed_their_login_is_still_one_user_under_the_same_id_with_the_new_login()
    {
        var fixture = new Fixture();
        await fixture.Service.AddAsync("alice", Token);

        var renamed = await fixture.Service.AddAsync("alice-renamed", Token);

        Assert.False(renamed.Changed);
        var stored = Assert.Single(fixture.Stored);
        Assert.Equal((1L, "alice-renamed"), (stored.UserId, stored.Login));
    }

    [Fact]
    public async Task A_login_GitHub_does_not_know_is_refused_and_nothing_is_stored()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.AddAsync("nobody", Token);

        Assert.Equal(TrustedUserFailure.UserNotFound, result.Failure);
        Assert.Empty(fixture.Stored);
    }

    [Fact]
    public async Task Hephaestuss_own_identity_is_refused_and_nothing_is_stored()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.AddAsync("hephaestus[bot]", Token);

        Assert.Equal(TrustedUserFailure.OwnIdentity, result.Failure);
        Assert.Empty(fixture.Stored);
    }

    [Theory]
    [InlineData("-bad")]
    [InlineData("a b")]
    [InlineData("")]
    public async Task A_login_of_no_shape_is_refused_without_asking_GitHub(string login)
    {
        var fixture = new Fixture();

        Assert.Equal(TrustedUserFailure.InvalidLogin, (await fixture.Service.AddAsync(login, Token)).Failure);
        Assert.Equal(TrustedUserFailure.InvalidLogin, (await fixture.Service.RemoveAsync(login, Token)).Failure);
        Assert.Empty(fixture.Lookups);
    }

    [Theory]
    [InlineData(GitHubUserFailure.NotConnected, TrustedUserFailure.NotConnected)]
    [InlineData(GitHubUserFailure.KeyUnreadable, TrustedUserFailure.KeyUnreadable)]
    [InlineData(GitHubUserFailure.Rejected, TrustedUserFailure.Rejected)]
    [InlineData(GitHubUserFailure.Unreachable, TrustedUserFailure.Unreachable)]
    [InlineData(GitHubUserFailure.NotFound, TrustedUserFailure.UserNotFound)]
    public async Task A_lookup_that_fails_fails_the_add_with_its_own_cause_and_stores_nothing(GitHubUserFailure lookup, TrustedUserFailure expected)
    {
        var fixture = new Fixture { LookupFails = lookup };

        var result = await fixture.Service.AddAsync("alice", Token);

        Assert.Equal(expected, result.Failure);
        Assert.Empty(fixture.Stored);
    }

    [Fact]
    public async Task Removing_a_listed_login_removes_that_user_without_asking_GitHub()
    {
        var fixture = new Fixture { LookupFails = GitHubUserFailure.Unreachable };
        fixture.Stored.AddRange([new TrustedUser(1, "Alice", DateTimeOffset.UnixEpoch), new TrustedUser(2, "bob", DateTimeOffset.UnixEpoch)]);

        var result = await fixture.Service.RemoveAsync("alice", Token);

        Assert.Equal(TrustedUserFailure.None, result.Failure);
        Assert.Equal((1L, "Alice"), (result.User!.UserId, result.User.Login));
        Assert.Equal([2L], fixture.Stored.Select(u => u.UserId));
        Assert.Empty(fixture.Lookups);
    }

    [Fact]
    public async Task Removing_by_the_new_login_of_a_renamed_user_finds_the_id_through_GitHub()
    {
        var fixture = new Fixture();
        fixture.Stored.Add(new TrustedUser(1, "alice-old-name", DateTimeOffset.UnixEpoch));

        var result = await fixture.Service.RemoveAsync("alice-renamed", Token);

        Assert.Equal(TrustedUserFailure.None, result.Failure);
        Assert.Equal("alice-old-name", result.User!.Login);
        Assert.Empty(fixture.Stored);
    }

    [Fact]
    public async Task When_two_stored_users_have_the_same_login_GitHub_says_which_is_meant()
    {
        var fixture = new Fixture();
        fixture.Stored.AddRange([new TrustedUser(1, "bob", DateTimeOffset.UnixEpoch), new TrustedUser(2, "bob", DateTimeOffset.UnixEpoch)]);

        var result = await fixture.Service.RemoveAsync("bob", Token);

        Assert.Equal(TrustedUserFailure.None, result.Failure);
        Assert.Equal([1L], fixture.Stored.Select(u => u.UserId));
        Assert.Equal(["bob"], fixture.Lookups);
    }

    [Fact]
    public async Task Two_stored_users_with_one_login_and_GitHub_unreachable_removes_neither()
    {
        var fixture = new Fixture { LookupFails = GitHubUserFailure.Unreachable };
        fixture.Stored.AddRange([new TrustedUser(1, "bob", DateTimeOffset.UnixEpoch), new TrustedUser(2, "bob", DateTimeOffset.UnixEpoch)]);

        var result = await fixture.Service.RemoveAsync("bob", Token);

        Assert.Equal(TrustedUserFailure.Unreachable, result.Failure);
        Assert.Equal(2, fixture.Stored.Count);
    }

    [Fact]
    public async Task Removing_someone_who_is_not_trusted_says_so()
    {
        var fixture = new Fixture();
        fixture.Stored.Add(new TrustedUser(2, "bob", DateTimeOffset.UnixEpoch));

        Assert.Equal(TrustedUserFailure.NotTrusted, (await fixture.Service.RemoveAsync("alice", Token)).Failure);
        Assert.Equal(TrustedUserFailure.NotTrusted, (await fixture.Service.RemoveAsync("nobody", Token)).Failure);
        Assert.Equal([2L], fixture.Stored.Select(u => u.UserId));
    }

    [Fact]
    public async Task Removing_from_an_empty_list_says_nobody_is_trusted_without_asking_GitHub()
    {
        var fixture = new Fixture { LookupFails = GitHubUserFailure.NotConnected };

        Assert.Equal(TrustedUserFailure.NotTrusted, (await fixture.Service.RemoveAsync("alice", Token)).Failure);
        Assert.Empty(fixture.Lookups);
    }

    [Fact]
    public async Task Removing_an_unlisted_login_while_GitHub_fails_reports_that_failure()
    {
        var fixture = new Fixture { LookupFails = GitHubUserFailure.Rejected };
        fixture.Stored.Add(new TrustedUser(2, "bob", DateTimeOffset.UnixEpoch));

        Assert.Equal(TrustedUserFailure.Rejected, (await fixture.Service.RemoveAsync("alice", Token)).Failure);
    }

    [Fact]
    public async Task A_user_removed_by_someone_else_in_between_is_not_trusted()
    {
        var fixture = new Fixture();
        fixture.Stored.Add(new TrustedUser(1, "Alice", DateTimeOffset.UnixEpoch));
        var racing = new RacingConfiguration(fixture);

        var result = await new TrustedUserService(fixture, fixture, racing).RemoveAsync("alice", Token);

        Assert.Equal(TrustedUserFailure.NotTrusted, result.Failure);
    }

    /// <summary>A configuration whose removal finds the user already gone.</summary>
    private sealed class RacingConfiguration(Fixture inner) : IInstallationConfiguration
    {
        public Task SetGitHubAppIdAsync(long appId, string slug, CancellationToken cancellationToken) => inner.SetGitHubAppIdAsync(appId, slug, cancellationToken);

        public Task<TrustedUserWrite> AddTrustedUserAsync(long userId, string login, CancellationToken cancellationToken) => inner.AddTrustedUserAsync(userId, login, cancellationToken);

        public Task<bool> RemoveTrustedUserAsync(long userId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<AliasWrite> AddAliasAsync(Alias alias, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Alias?> RemoveAliasAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallationLimits> SetLimitsAsync(LimitsChange change, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> AddProtectedPathAsync(string glob, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> RemoveProtectedPathAsync(string glob, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetSettingAsync(string name, string value, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ProjectWrite> RegisterProjectAsync(long repositoryId, string fullName, string baseBranch, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Project?> RemoveProjectAsync(long repositoryId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
