using System.Net;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Octokit;
using Octokit.Internal;

namespace Hephaestus.GitHub;

/// <summary>
/// The gateway to GitHub (AD-4), the only code that holds the App credential or names Octokit. This story gives it Hephaestus's
/// own identity: authenticate as the App with a signed token, read the App, its installations, and the repositories of each, and
/// judge the permissions (AD-19). It only reads. The installation token it asks for to list repositories is held for that call,
/// never stored or logged.
/// </summary>
public sealed class GitHubGateway : ICodeHost
{
    private const int PageSize = 100;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    // The command line gives up after 30 seconds, so a read that is still going by then is reported here, as unreachable, while the
    // host has not yet stored anything, instead of being stored after the command line has told the Owner it failed.
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(25);

    private readonly ISecretStore _secrets;
    private readonly IReadStore _reads;
    private readonly IInstallationConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly Func<HttpMessageHandler> _handler;
    private readonly TimeSpan _deadline;

    // The key and the App id are two stores; one gate keeps a second connect, or a check, from seeing half of a change.
    private readonly SemaphoreSlim _credential = new(1, 1);

    /// <param name="handler">The transport; a test gives its own. The default is Octokit's, which honours the proxy settings.</param>
    /// <param name="deadline">How long one read of the identity may take altogether; a test shortens it.</param>
    public GitHubGateway(
        ISecretStore secrets,
        IReadStore reads,
        IInstallationConfiguration configuration,
        TimeProvider time,
        Func<HttpMessageHandler>? handler = null,
        TimeSpan? deadline = null)
    {
        _secrets = secrets;
        _reads = reads;
        _configuration = configuration;
        _time = time;
        _handler = handler ?? (() => HttpMessageHandlerFactory.CreateDefault());
        _deadline = deadline ?? DefaultDeadline;
    }

    public async Task<GitHubIdentityResult> ConnectIdentityAsync(long appId, string privateKey, CancellationToken cancellationToken)
    {
        var result = await ReadIdentityAsync(appId, privateKey, cancellationToken);
        if (result.Failure != GitHubIdentityFailure.None)
        {
            return result;
        }

        // GitHub accepted both. From here a failure undoes what was written, so the key and the id are either both the new ones or
        // both what they were.
        await _credential.WaitAsync(cancellationToken);
        try
        {
            var earlierKey = await _secrets.ReadAsync(GitHubAppCredential.PrivateKeySecretName, cancellationToken);
            try
            {
                await _secrets.SetAsync(GitHubAppCredential.PrivateKeySecretName, privateKey, cancellationToken);
                await _configuration.SetGitHubAppIdAsync(appId, cancellationToken);
            }
            catch
            {
                await RestoreKeyAsync(earlierKey);
                throw;
            }
        }
        finally
        {
            _credential.Release();
        }

        return result;
    }

    public async Task<GitHubIdentityResult> CheckIdentityAsync(CancellationToken cancellationToken)
    {
        long? appId;
        string? privateKey;
        await _credential.WaitAsync(cancellationToken);
        try
        {
            appId = await _reads.GetGitHubAppIdAsync(cancellationToken);
            privateKey = await _secrets.ReadAsync(GitHubAppCredential.PrivateKeySecretName, cancellationToken);
        }
        finally
        {
            _credential.Release();
        }

        return appId is null || privateKey is null
            ? GitHubIdentityResult.Failed(GitHubIdentityFailure.NotConnected)
            : await ReadIdentityAsync(appId.Value, privateKey, cancellationToken);
    }

    private async Task RestoreKeyAsync(string? earlierKey)
    {
        // The undo must not be stopped by the request that failed, and must not hide the failure that started it.
        try
        {
            if (earlierKey is null)
            {
                await _secrets.RemoveAsync(GitHubAppCredential.PrivateKeySecretName, CancellationToken.None);
            }
            else
            {
                await _secrets.SetAsync(GitHubAppCredential.PrivateKeySecretName, earlierKey, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // The first failure is the one to report; a repeat of the command settles what is left.
        }
    }

    private async Task<GitHubIdentityResult> ReadIdentityAsync(long appId, string privateKey, CancellationToken cancellationToken)
    {
        var jwt = GitHubAppJwt.TryCreate(appId, privateKey, _time.GetUtcNow());
        if (jwt is null)
        {
            return GitHubIdentityResult.Failed(GitHubIdentityFailure.KeyUnreadable);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_deadline);
        using var transport = new HttpClientAdapter(_handler);
        transport.SetRequestTimeout(RequestTimeout);
        try
        {
            var client = CreateClient(new Credentials(jwt, AuthenticationType.Bearer), transport);
            var app = await client.GitHubApps.GetCurrent().WaitAsync(limit.Token);
            var installations = await client.GitHubApps.GetAllInstallationsForCurrent(new ApiOptions { PageSize = PageSize }).WaitAsync(limit.Token);

            var places = new List<GitHubInstallation>();
            foreach (var installation in installations.OrderBy(i => i.Account?.Login, StringComparer.Ordinal).ThenBy(i => i.Id))
            {
                places.Add(await ReadInstallationAsync(client, transport, installation, limit.Token));
            }

            // What the App has been granted is what the App and every installation of it agree on: a change that an installation
            // has not accepted yet is not in force there, and an old grant it still holds is.
            var levels = new List<Dictionary<string, GitHubPermissionLevel>> { Levels(app.Permissions) };
            levels.AddRange(installations.Where(i => i.Permissions is not null).Select(i => Levels(i.Permissions)));
            var (permissions, warnings) = GitHubPermissionPolicy.Evaluate(Combine(levels));
            return GitHubIdentityResult.Succeeded(new GitHubIdentity(app.Id, app.Slug, app.Name, app.Owner?.Login ?? string.Empty, places, permissions, warnings));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsOutage(exception))
        {
            return GitHubIdentityResult.Failed(GitHubIdentityFailure.Unreachable);
        }
        catch (ApiException)
        {
            return GitHubIdentityResult.Failed(GitHubIdentityFailure.Rejected);
        }
    }

    private async Task<GitHubInstallation> ReadInstallationAsync(GitHubClient app, IHttpClient transport, Installation installation, CancellationToken cancellationToken)
    {
        var account = installation.Account?.Login ?? string.Empty;
        var type = installation.TargetType.StringValue;
        var selection = installation.RepositorySelection.StringValue;
        if (installation.SuspendedAt is not null)
        {
            return new GitHubInstallation(installation.Id, account, type, selection, Suspended: true, []);
        }

        var token = await app.GitHubApps.CreateInstallationToken(installation.Id).WaitAsync(cancellationToken);
        var repositories = await CreateClient(new Credentials(token.Token), transport).GitHubApps.Installation
            .GetAllRepositoriesForCurrent(new ApiOptions { PageSize = PageSize }).WaitAsync(cancellationToken);
        var names = repositories.Repositories.Select(r => r.FullName).Order(StringComparer.Ordinal).ToList();
        return new GitHubInstallation(installation.Id, account, type, selection, Suspended: false, names);
    }

    private static GitHubClient CreateClient(Credentials credentials, IHttpClient transport)
    {
        var connection = new Connection(
            new Octokit.ProductHeaderValue("Hephaestus", Core.HephaestusVersion.Current),
            GitHubClient.GitHubApiUrl,
            new InMemoryCredentialStore(credentials),
            transport,
            new SimpleJsonSerializer());
        return new GitHubClient(connection);
    }

    /// <summary>
    /// GitHub failing to answer: no connection, a timeout, a server error, or a rate limit (a 429, or a 403 that Octokit recognises as
    /// a primary or secondary limit). Anything else GitHub says is a refusal of what it was given.
    /// </summary>
    private static bool IsOutage(Exception exception) => exception switch
    {
        HttpRequestException or TimeoutException or TaskCanceledException => true,
        RateLimitExceededException or SecondaryRateLimitExceededException or AbuseException => true,
        ApiException api => (int)api.StatusCode >= 500 || api.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout,
        _ => false,
    };

    /// <summary>The level each permission has when several parties hold it: the lowest of what is needed, the highest of what must not be had.</summary>
    private static Dictionary<string, GitHubPermissionLevel> Combine(List<Dictionary<string, GitHubPermissionLevel>> holders)
    {
        var combined = new Dictionary<string, GitHubPermissionLevel>();
        foreach (var name in holders.SelectMany(h => h.Keys).Distinct())
        {
            var levels = holders.Select(h => h.GetValueOrDefault(name, GitHubPermissionLevel.None)).ToList();
            combined[name] = GitHubPermissionPolicy.Forbidden.Contains(name) ? levels.Max() : levels.Min();
        }

        return combined;
    }

    private static Dictionary<string, GitHubPermissionLevel> Levels(InstallationPermissions? granted)
    {
        var levels = new Dictionary<string, GitHubPermissionLevel>();
        if (granted is null)
        {
            return levels;
        }

        void Add<T>(string name, StringEnum<T>? level)
            where T : struct, Enum
        {
            if (level is { } known)
            {
                levels[name] = known.StringValue switch
                {
                    "read" => GitHubPermissionLevel.Read,
                    "write" => GitHubPermissionLevel.Write,
                    "admin" => GitHubPermissionLevel.Admin,
                    _ => GitHubPermissionLevel.None,
                };
            }
        }

        Add(GitHubPermissionPolicy.Contents, granted.Contents);
        Add(GitHubPermissionPolicy.Issues, granted.Issues);
        Add(GitHubPermissionPolicy.PullRequests, granted.PullRequests);
        Add(GitHubPermissionPolicy.Metadata, granted.Metadata);
        Add(GitHubPermissionPolicy.Checks, granted.Checks);
        Add(GitHubPermissionPolicy.Statuses, granted.Statuses);
        Add(GitHubPermissionPolicy.Workflows, granted.Workflows);
        Add(GitHubPermissionPolicy.Administration, granted.Administration);
        Add(GitHubPermissionPolicy.OrganizationAdministration, granted.OrganizationAdministration);
        return levels;
    }
}
