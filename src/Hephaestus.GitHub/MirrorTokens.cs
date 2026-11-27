using System.Net;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Octokit;

namespace Hephaestus.GitHub;

/// <summary>The installation token that reads one repository, or why there is none. The token is held in memory only and goes nowhere but the environment of a git process.</summary>
internal sealed record MirrorToken(MirrorFailure Failure, string? Token)
{
    public static MirrorToken Of(string token) => new(MirrorFailure.None, token);

    public static MirrorToken Failed(MirrorFailure failure) => new(failure, null);
}

/// <summary>
/// Finds the installation token for a repository, as the poller does (AD-4): the App's credential is read, a token is asked for each installation and kept until shortly
/// before it ends, and the installation that holds the repository gives it. A rate limit that GitHub states closes the gate the poller and the gateway share.
/// </summary>
internal sealed class MirrorTokens(IReadStore reads, ISecretStore secrets, TimeProvider time, GitHubRateLimit limit, Func<HttpMessageHandler> handler)
{
    private readonly GitHubInstallations _installations = new(time, handler);

    // One token request at a time: two steps starting together would ask GitHub for the installations twice.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<MirrorToken> TokenAsync(long repositoryId, CancellationToken cancellationToken)
    {
        if (limit.ClosedUntil is not null)
        {
            return MirrorToken.Failed(MirrorFailure.Unreachable);
        }

        var appId = await reads.GetGitHubAppIdAsync(cancellationToken);
        var privateKey = await secrets.ReadAsync(GitHubAppCredential.PrivateKeySecretName, cancellationToken);
        if (appId is null || privateKey is null)
        {
            return MirrorToken.Failed(MirrorFailure.NotConnected);
        }

        var jwt = GitHubAppJwt.TryCreate(appId.Value, privateKey, time.GetUtcNow());
        if (jwt is null)
        {
            return MirrorToken.Failed(MirrorFailure.KeyUnreadable);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_installations.IsStale)
            {
                await _installations.LoadAsync(jwt, cancellationToken);
            }

            var token = _installations.TokenFor(repositoryId);
            if (token is null && _installations.MaySearchAgain)
            {
                await _installations.LoadAsync(jwt, cancellationToken);
                token = _installations.TokenFor(repositoryId);
            }

            return token is null ? MirrorToken.Failed(MirrorFailure.NotInstalled) : MirrorToken.Of(token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && GitHubGateway.IsOutage(exception))
        {
            if (GitHubRateLimit.WaitOf(exception, time.GetUtcNow()) is { } until)
            {
                limit.CloseUntil(until);
            }

            return MirrorToken.Failed(MirrorFailure.Unreachable);
        }
        catch (ApiException exception)
        {
            if (exception.StatusCode == HttpStatusCode.Unauthorized)
            {
                _installations.Forget();
            }

            return MirrorToken.Failed(MirrorFailure.Rejected);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the tokens, so that the next request asks GitHub again: used when git says GitHub did not accept the one it was given.</summary>
    public void Forget() => _installations.Forget();
}
