using System.Net;
using System.Security.Cryptography;
using Hephaestus.Core.GitHub;

namespace Hephaestus.GitHub.Tests;

/// <summary>Story 1.7, AD-4, AD-19: the gateway authenticates as the App, reads its identity and permissions, and stores the credential only when GitHub accepted it.</summary>
public class GitHubGatewayTests
{
    private const long AppId = 123456;
    private const string KeyName = GitHubAppCredential.PrivateKeySecretName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string NewKey() => RSA.Create(2048).ExportRSAPrivateKeyPem();

    private static GitHubGateway GatewayOver(FakeStores stores, FakeGitHub github) =>
        new(stores, stores, stores, new FixedTime(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero)), () => github);

    [Fact]
    public async Task Connecting_authenticates_as_the_App_and_reports_who_it_is_and_where_it_is_installed()
    {
        var stores = new FakeStores();
        var github = new FakeGitHub();

        var result = await GatewayOver(stores, github).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.None, result.Failure);
        var identity = result.Identity!;
        Assert.Equal((123456, "hephaestus-test", "Hephaestus Test", "adamcharusta"), (identity.AppId, identity.Slug, identity.Name, identity.Owner));
        Assert.Collection(
            identity.Installations,
            acme =>
            {
                Assert.Equal((10, "acme", "Organization", "all", false), (acme.Id, acme.Account, acme.AccountType, acme.RepositorySelection, acme.Suspended));
                Assert.Equal(["acme/api", "acme/web"], acme.Repositories);
            },
            own =>
            {
                Assert.Equal((11, "adamcharusta", "User", "selected"), (own.Id, own.Account, own.AccountType, own.RepositorySelection));
                Assert.Equal(["adamcharusta/Hephaestus"], own.Repositories);
            });
    }

    [Fact]
    public async Task The_App_is_authenticated_with_a_signed_token_and_the_repositories_are_listed_with_an_installation_token()
    {
        var github = new FakeGitHub();

        await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token);

        var app = github.Requests.Single(r => r.Path == "/app");
        Assert.StartsWith("Bearer ", app.Authorization, StringComparison.Ordinal);
        Assert.Equal(3, app.Authorization!["Bearer ".Length..].Split('.').Length);
        var repositories = github.Requests.Where(r => r.Path == "/installation/repositories").ToList();
        Assert.Equal(2, repositories.Count);
        Assert.All(repositories, r => Assert.EndsWith(FakeGitHub.InstallationToken, r.Authorization, StringComparison.Ordinal));
        Assert.All(github.Requests, r => Assert.Contains(r.Method.Method, new[] { "GET", "POST" }));
    }

    [Fact]
    public async Task Connecting_stores_the_key_as_a_secret_and_the_id_as_configuration_only_after_GitHub_accepted_both()
    {
        var stores = new FakeStores();
        var key = NewKey();

        await GatewayOver(stores, new FakeGitHub()).ConnectIdentityAsync(AppId, key, Token);

        Assert.Equal(key, stores.Secrets[KeyName]);
        Assert.Equal(AppId, stores.AppId);
        Assert.Equal([KeyName], stores.Secrets.Keys);
    }

    [Fact]
    public async Task Connecting_keeps_the_slug_of_the_App_beside_its_id_so_the_loop_can_tell_its_own_bot_account()
    {
        var stores = new FakeStores();

        await GatewayOver(stores, new FakeGitHub()).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal("hephaestus-test", stores.AppSlug);
    }

    [Fact]
    public async Task Checking_an_App_connected_before_its_slug_was_kept_brings_the_slug_up_to_date()
    {
        var stores = new FakeStores { AppId = AppId, Secrets = { [KeyName] = NewKey() } };

        await GatewayOver(stores, new FakeGitHub()).CheckIdentityAsync(Token);

        Assert.Equal(("hephaestus-test", AppId, 1), (stores.AppSlug, stores.AppId, stores.AppIdWrites));
    }

    [Fact]
    public async Task Checking_an_App_whose_slug_GitHub_has_changed_replaces_the_slug_and_one_that_is_current_writes_nothing()
    {
        var stores = new FakeStores { AppId = AppId, AppSlug = "the-old-name", Secrets = { [KeyName] = NewKey() } };
        var gateway = GatewayOver(stores, new FakeGitHub());

        await gateway.CheckIdentityAsync(Token);
        await gateway.CheckIdentityAsync(Token);

        Assert.Equal(("hephaestus-test", 1), (stores.AppSlug, stores.AppIdWrites));
    }

    [Fact]
    public async Task Checking_a_credential_GitHub_refuses_writes_nothing()
    {
        var stores = new FakeStores { AppId = AppId, Secrets = { [KeyName] = NewKey() } };
        var github = new FakeGitHub { AppStatus = HttpStatusCode.Unauthorized };

        await GatewayOver(stores, github).CheckIdentityAsync(Token);

        Assert.Equal((null, 0), (stores.AppSlug, stores.AppIdWrites));
    }

    [Fact]
    public async Task Checking_uses_the_stored_credential_and_reads_the_same_identity()
    {
        var stores = new FakeStores();
        var github = new FakeGitHub();
        var gateway = GatewayOver(stores, github);
        var connected = await gateway.ConnectIdentityAsync(AppId, NewKey(), Token);
        github.Requests.Clear();

        var checkedNow = await gateway.CheckIdentityAsync(Token);

        Assert.Equal(GitHubIdentityFailure.None, checkedNow.Failure);
        Assert.Equal(connected.Identity!.AppId, checkedNow.Identity!.AppId);
        Assert.Equal(connected.Identity.Installations.Select(i => i.Repositories), checkedNow.Identity.Installations.Select(i => i.Repositories));
        Assert.Contains(github.Requests, r => r.Path == "/app");
    }

    [Fact]
    public async Task Checking_with_nothing_stored_says_so_and_does_not_ask_GitHub()
    {
        var github = new FakeGitHub();

        var onlyKey = new FakeStores { Secrets = { [KeyName] = NewKey() } };
        var onlyId = new FakeStores { AppId = AppId };

        Assert.Equal(GitHubIdentityFailure.NotConnected, (await GatewayOver(new FakeStores(), github).CheckIdentityAsync(Token)).Failure);
        Assert.Equal(GitHubIdentityFailure.NotConnected, (await GatewayOver(onlyKey, github).CheckIdentityAsync(Token)).Failure);
        Assert.Equal(GitHubIdentityFailure.NotConnected, (await GatewayOver(onlyId, github).CheckIdentityAsync(Token)).Failure);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task Every_required_permission_is_present_and_nothing_is_warned_of_when_the_App_has_exactly_those()
    {
        var result = await GatewayOver(new FakeStores(), new FakeGitHub()).ConnectIdentityAsync(AppId, NewKey(), Token);

        var identity = result.Identity!;
        Assert.True(identity.Ready);
        Assert.Equal(
            ["contents:write", "issues:write", "pull-requests:write", "metadata:read", "checks:read", "statuses:read"],
            identity.Permissions.Select(p => $"{p.Name}:{p.Granted.ToString().ToLowerInvariant()}"));
        Assert.Empty(identity.Warnings);
    }

    [Fact]
    public async Task A_missing_or_too_low_permission_is_reported_as_missing()
    {
        var github = new FakeGitHub
        {
            AppJson = """{"id":123456,"slug":"s","name":"N","owner":{"login":"o","id":1},"permissions":{"contents":"read","issues":"write","metadata":"read","statuses":"read"}}""",
        };

        var identity = (await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token)).Identity!;

        Assert.False(identity.Ready);
        Assert.Equal(["contents", "pull-requests", "checks"], identity.Permissions.Where(p => !p.Present).Select(p => p.Name));
        Assert.Equal(GitHubPermissionLevel.Read, identity.Permissions.Single(p => p.Name == "contents").Granted);
        Assert.Equal(GitHubPermissionLevel.None, identity.Permissions.Single(p => p.Name == "checks").Granted);
    }

    [Fact]
    public async Task Workflows_and_administration_are_warned_about_with_their_levels()
    {
        var github = new FakeGitHub
        {
            AppJson = """
                {"id":123456,"slug":"s","name":"N","owner":{"login":"o","id":1},"permissions":
                {"contents":"write","issues":"write","pull_requests":"write","metadata":"read","checks":"read","statuses":"read","workflows":"write","administration":"read","organization_administration":"admin"}}
                """,
        };

        var identity = (await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token)).Identity!;

        Assert.True(identity.Ready);
        Assert.Equal(
            [new GitHubPermissionWarning("workflows", GitHubPermissionLevel.Write), new GitHubPermissionWarning("administration", GitHubPermissionLevel.Read), new GitHubPermissionWarning("organization-administration", GitHubPermissionLevel.Admin)],
            identity.Warnings);
    }

    [Fact]
    public async Task A_suspended_installation_is_reported_and_its_repositories_are_not_asked_for()
    {
        var github = new FakeGitHub
        {
            InstallationsJson = $$"""[{"id":10,"account":{"login":"acme","id":2,"type":"Organization"},"target_type":"Organization","repository_selection":"all","suspended_at":"2026-11-01T00:00:00Z","permissions":{{FakeGitHub.Permissions}}}]""",
        };

        var identity = (await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token)).Identity!;

        var installation = Assert.Single(identity.Installations);
        Assert.True(installation.Suspended);
        Assert.Empty(installation.Repositories);
        Assert.DoesNotContain(github.Requests, r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_App_with_no_installation_is_a_valid_identity_with_no_repositories()
    {
        var github = new FakeGitHub { InstallationsJson = "[]" };

        var result = await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.None, result.Failure);
        Assert.Empty(result.Identity!.Installations);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_credential_GitHub_refuses_is_rejected_and_nothing_is_stored(HttpStatusCode status)
    {
        var stores = new FakeStores();

        var result = await GatewayOver(stores, new FakeGitHub { AppStatus = status }).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.Rejected, result.Failure);
        Assert.Null(result.Identity);
        Assert.Empty(stores.Secrets);
        Assert.Null(stores.AppId);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_GitHub_that_cannot_serve_is_unreachable_and_nothing_is_stored(HttpStatusCode status)
    {
        var stores = new FakeStores();

        var result = await GatewayOver(stores, new FakeGitHub { AppStatus = status }).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, result.Failure);
        Assert.Empty(stores.Secrets);
        Assert.Null(stores.AppId);
    }

    [Fact]
    public async Task A_rate_limit_is_not_a_refused_credential()
    {
        var github = new FakeGitHub
        {
            Override = _ =>
            {
                var response = FakeGitHub.Json(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded for installation."}""");
                response.Headers.Add("X-RateLimit-Limit", "60");
                response.Headers.Add("X-RateLimit-Remaining", "0");
                response.Headers.Add("X-RateLimit-Reset", "4102444800");
                return response;
            },
        };

        var result = await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_secondary_rate_limit_is_not_a_refused_credential()
    {
        var github = new FakeGitHub
        {
            Override = _ => FakeGitHub.Json(HttpStatusCode.Forbidden, """{"message":"You have exceeded a secondary rate limit. Please wait a few minutes before you try again."}"""),
        };

        var result = await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_request_timeout_answer_is_unreachable()
    {
        var result = await GatewayOver(new FakeStores(), new FakeGitHub { AppStatus = HttpStatusCode.RequestTimeout }).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_GitHub_that_never_answers_is_unreachable_within_the_deadline_and_nothing_is_stored()
    {
        var stores = new FakeStores();
        var gateway = new GitHubGateway(stores, stores, stores, TimeProvider.System, () => new FakeGitHub { Hang = true }, TimeSpan.FromMilliseconds(200));

        var started = DateTimeOffset.UtcNow;
        var result = await gateway.ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, result.Failure);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.Empty(stores.Secrets);
        Assert.Null(stores.AppId);
    }

    [Fact]
    public async Task A_permission_an_installation_has_not_accepted_yet_is_missing()
    {
        var github = new FakeGitHub
        {
            InstallationsJson = """
                [{"id":10,"account":{"login":"acme","id":2,"type":"Organization"},"target_type":"Organization","repository_selection":"all",
                "permissions":{"contents":"write","issues":"write","pull_requests":"read","metadata":"read","checks":"read","statuses":"read"}}]
                """,
        };

        var identity = (await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token)).Identity!;

        var missing = Assert.Single(identity.Permissions, p => !p.Present);
        Assert.Equal(("pull-requests", GitHubPermissionLevel.Read), (missing.Name, missing.Granted));
    }

    [Fact]
    public async Task A_forbidden_permission_an_installation_still_holds_is_warned_about_though_the_App_no_longer_has_it()
    {
        var github = new FakeGitHub
        {
            InstallationsJson = """
                [{"id":10,"account":{"login":"acme","id":2,"type":"Organization"},"target_type":"Organization","repository_selection":"all",
                "permissions":{"contents":"write","issues":"write","pull_requests":"write","metadata":"read","checks":"read","statuses":"read","workflows":"write"}}]
                """,
        };

        var identity = (await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token)).Identity!;

        Assert.Equal(new GitHubPermissionWarning("workflows", GitHubPermissionLevel.Write), Assert.Single(identity.Warnings));
    }

    [Fact]
    public async Task An_answer_without_an_owner_or_permissions_is_not_a_crash()
    {
        var github = new FakeGitHub { AppJson = """{"id":123456,"slug":"s","name":"N"}""", InstallationsJson = "[]" };

        var identity = (await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token)).Identity!;

        Assert.Equal(string.Empty, identity.Owner);
        Assert.False(identity.Ready);
    }

    [Fact]
    public async Task Two_connects_at_once_leave_one_key_and_the_id_that_goes_with_it()
    {
        var stores = new FakeStores();
        var gateway = GatewayOver(stores, new FakeGitHub());
        var first = NewKey();
        var second = NewKey();

        await Task.WhenAll(gateway.ConnectIdentityAsync(1, first, Token), gateway.ConnectIdentityAsync(2, second, Token));

        Assert.True((stores.AppId == 1 && stores.Secrets[KeyName] == first) || (stores.AppId == 2 && stores.Secrets[KeyName] == second));
    }

    [Fact]
    public async Task A_network_failure_is_unreachable()
    {
        var github = new FakeGitHub { Override = _ => throw new HttpRequestException("No route to host") };
        var stores = new FakeStores();

        var result = await GatewayOver(stores, github).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, result.Failure);
        Assert.Empty(stores.Secrets);
    }

    [Fact]
    public async Task A_timeout_is_unreachable()
    {
        var github = new FakeGitHub { Override = _ => throw new TaskCanceledException("The request timed out") };

        var result = await GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_failure_while_listing_the_repositories_stores_nothing_either()
    {
        var github = new FakeGitHub
        {
            Override = request => request.RequestUri!.AbsolutePath == "/installation/repositories" ? FakeGitHub.Json(HttpStatusCode.ServiceUnavailable, "{}") : null,
        };
        var stores = new FakeStores();

        var result = await GatewayOver(stores, github).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, result.Failure);
        Assert.Empty(stores.Secrets);
        Assert.Null(stores.AppId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a pem key")]
    public async Task A_key_that_is_not_a_private_key_is_unreadable_and_GitHub_is_not_asked(string key)
    {
        var github = new FakeGitHub();
        var stores = new FakeStores();

        var result = await GatewayOver(stores, github).ConnectIdentityAsync(AppId, key, Token);

        Assert.Equal(GitHubIdentityFailure.KeyUnreadable, result.Failure);
        Assert.Empty(github.Requests);
        Assert.Empty(stores.Secrets);
        Assert.Null(stores.AppId);
    }

    [Fact]
    public async Task A_credential_that_is_rejected_leaves_the_one_stored_before()
    {
        var earlier = NewKey();
        var stores = new FakeStores { AppId = 99, Secrets = { [KeyName] = earlier } };

        var result = await GatewayOver(stores, new FakeGitHub { AppStatus = HttpStatusCode.Unauthorized }).ConnectIdentityAsync(AppId, NewKey(), Token);

        Assert.Equal(GitHubIdentityFailure.Rejected, result.Failure);
        Assert.Equal(earlier, stores.Secrets[KeyName]);
        Assert.Equal(99, stores.AppId);
    }

    [Fact]
    public async Task When_the_id_cannot_be_recorded_the_key_stored_before_is_restored_and_the_failure_is_reported()
    {
        var earlier = NewKey();
        var stores = new FakeStores { AppId = 99, Secrets = { [KeyName] = earlier }, FailToRecordAppId = new IOException("disk full") };

        var failure = await Assert.ThrowsAsync<IOException>(() => GatewayOver(stores, new FakeGitHub()).ConnectIdentityAsync(AppId, NewKey(), Token));

        Assert.Equal("disk full", failure.Message);
        Assert.Equal(earlier, stores.Secrets[KeyName]);
        Assert.Equal(99, stores.AppId);
    }

    [Fact]
    public async Task When_the_id_cannot_be_recorded_a_key_that_was_not_there_before_is_removed()
    {
        var stores = new FakeStores { FailToRecordAppId = new IOException("disk full") };

        await Assert.ThrowsAsync<IOException>(() => GatewayOver(stores, new FakeGitHub()).ConnectIdentityAsync(AppId, NewKey(), Token));

        Assert.Empty(stores.Secrets);
        Assert.Null(stores.AppId);
    }

    [Fact]
    public async Task When_the_key_cannot_be_stored_what_it_left_behind_is_undone()
    {
        var stores = new FakeStores { FailToStoreSecret = new IOException("read-only volume") };

        await Assert.ThrowsAsync<IOException>(() => GatewayOver(stores, new FakeGitHub()).ConnectIdentityAsync(AppId, NewKey(), Token));

        Assert.Empty(stores.Secrets);
        Assert.Null(stores.AppId);
    }

    [Fact]
    public async Task Cancelling_the_request_is_not_a_GitHub_outage()
    {
        using var source = new CancellationTokenSource();
        var github = new FakeGitHub { Override = _ => { source.Cancel(); throw new TaskCanceledException(); } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GatewayOver(new FakeStores(), github).ConnectIdentityAsync(AppId, NewKey(), source.Token));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
