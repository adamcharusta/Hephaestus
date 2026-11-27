using Octokit;
using Octokit.Internal;

namespace Hephaestus.GitHub;

/// <summary>
/// The installation tokens the poller reads with, and which repositories each installation holds. A token lasts an hour, so it is
/// kept until shortly before it ends instead of being asked for again every cycle; the list of repositories is read again now and
/// then, so that a repository the App was installed on later is found. Tokens are held in memory only.
/// </summary>
internal sealed class GitHubInstallations(TimeProvider time, Func<HttpMessageHandler> handler)
{
    private const int PageSize = 100;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ListLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How soon the lists may be read again to look for a repository that none of them holds.</summary>
    private static readonly TimeSpan SearchEvery = TimeSpan.FromMinutes(1);

    private sealed record Held(string Token, DateTimeOffset ExpiresAt, HashSet<long> RepositoryIds);

    private List<Held> _held = [];
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    /// <summary>Whether the tokens or the lists are too old to read with.</summary>
    public bool IsStale
    {
        get
        {
            var now = time.GetUtcNow();
            return _loadedAt == DateTimeOffset.MinValue || now - _loadedAt > ListLifetime || _held.Any(h => h.ExpiresAt - now < RenewBefore);
        }
    }

    /// <summary>Whether it is long enough since the lists were read that reading them again to find a missing repository is fair.</summary>
    public bool MaySearchAgain => time.GetUtcNow() - _loadedAt >= SearchEvery;

    /// <summary>The token of the installation that holds the repository, or null when no installation does.</summary>
    public string? TokenFor(long repositoryId) => _held.FirstOrDefault(h => h.RepositoryIds.Contains(repositoryId))?.Token;

    /// <summary>Forgets everything, so that the next cycle reads it again; used when GitHub no longer accepts a token.</summary>
    public void Forget()
    {
        _held = [];
        _loadedAt = DateTimeOffset.MinValue;
    }

    /// <summary>Reads the App's installations, a token for each that is not suspended, and the repositories each holds.</summary>
    public async Task LoadAsync(string jwt, CancellationToken cancellationToken)
    {
        using var transport = new HttpClientAdapter(handler);
        transport.SetRequestTimeout(RequestTimeout);
        var app = GitHubGateway.CreateClient(new Credentials(jwt, AuthenticationType.Bearer), transport);
        var installations = await app.GitHubApps.GetAllInstallationsForCurrent(new ApiOptions { PageSize = PageSize }).WaitAsync(cancellationToken);

        var held = new List<Held>();
        foreach (var installation in installations.Where(i => i.SuspendedAt is null).OrderBy(i => i.Id))
        {
            try
            {
                var token = await app.GitHubApps.CreateInstallationToken(installation.Id).WaitAsync(cancellationToken);
                var client = GitHubGateway.CreateClient(new Credentials(token.Token), transport);
                var repositories = await client.GitHubApps.Installation.GetAllRepositoriesForCurrent(new ApiOptions { PageSize = PageSize }).WaitAsync(cancellationToken);
                held.Add(new Held(token.Token, token.ExpiresAt, [.. repositories.Repositories.Select(r => r.Id)]));
            }
            catch (ApiException exception) when (!GitHubGateway.IsOutage(exception))
            {
                // GitHub refused this installation (its permissions were changed, say); the Projects of the others are still read.
            }
        }

        _held = held;
        _loadedAt = time.GetUtcNow();
    }
}
