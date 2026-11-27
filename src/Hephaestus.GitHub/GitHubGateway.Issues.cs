using System.Net;
using Hephaestus.Core.GitHub;
using Octokit;
using Octokit.Internal;

namespace Hephaestus.GitHub;

/// <summary>
/// The gateway's work on the labels of an issue (Story 2.4, AD-4, AD-5): a read of the labels, and a change that adds and removes labels as
/// a set operation. Which labels are wanted is decided by the caller; the gateway only asks GitHub, and tells a wait or an outage, which
/// are tried again, from a refusal, which is for good.
/// </summary>
public sealed partial class GitHubGateway
{
    public Task<GitHubIssueResult> ReadIssueLabelsAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken) =>
        OnIssueAsync(repositoryId, async (client, token) => GitHubIssueResult.Labelled(await LabelsOfAsync(client, repositoryId, issueNumber, token)), cancellationToken);

    public Task<GitHubIssueResult> ChangeIssueLabelsAsync(
        long repositoryId,
        int issueNumber,
        IReadOnlyList<string> add,
        IReadOnlyList<string> remove,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // A label has no marker to carry the key, so the key is not sent: adding a label that is there, and removing one that is not, change
        // nothing, which is what makes the change safe to repeat.
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        return OnIssueAsync(
            repositoryId,
            async (client, token) =>
            {
                // The new label goes on before the old one comes off, so that the issue is never without a state label in between.
                if (add.Count > 0)
                {
                    await client.Issue.Labels.AddToIssue(repositoryId, issueNumber, [.. add]).WaitAsync(token);
                }

                foreach (var label in remove)
                {
                    try
                    {
                        await client.Issue.Labels.RemoveFromIssue(repositoryId, issueNumber, label).WaitAsync(token);
                    }
                    catch (NotFoundException)
                    {
                        // Someone took it off meanwhile, which is what was wanted. An issue that is gone is found out by the read below.
                    }
                }

                return GitHubIssueResult.Labelled(await LabelsOfAsync(client, repositoryId, issueNumber, token));
            },
            cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> LabelsOfAsync(GitHubClient client, long repositoryId, int issueNumber, CancellationToken cancellationToken)
    {
        var labels = await client.Issue.Labels.GetAllForIssue(repositoryId, issueNumber, new ApiOptions { PageSize = PageSize }).WaitAsync(cancellationToken);
        return [.. labels.Select(label => label.Name)];
    }

    /// <summary>
    /// Runs <paramref name="action"/> with the token of the installation that holds the repository (found by its id, which a rename does not
    /// change). The credential is held for the call and never stored or logged.
    /// </summary>
    private async Task<GitHubIssueResult> OnIssueAsync(long repositoryId, Func<GitHubClient, CancellationToken, Task<GitHubIssueResult>> action, CancellationToken cancellationToken)
    {
        if (_limit.ClosedUntil is { } closed)
        {
            return GitHubIssueResult.Failed(GitHubIssueFailure.RateLimited, closed);
        }

        var (appId, privateKey) = await ReadCredentialAsync(cancellationToken);
        if (appId is null || privateKey is null)
        {
            return GitHubIssueResult.Failed(GitHubIssueFailure.NotConnected);
        }

        var jwt = GitHubAppJwt.TryCreate(appId.Value, privateKey, _time.GetUtcNow());
        if (jwt is null)
        {
            return GitHubIssueResult.Failed(GitHubIssueFailure.KeyUnreadable);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_deadline);
        using var transport = new HttpClientAdapter(_handler);
        transport.SetRequestTimeout(RequestTimeout);
        try
        {
            var app = CreateClient(new Credentials(jwt, AuthenticationType.Bearer), transport);
            var installations = await app.GitHubApps.GetAllInstallationsForCurrent(new ApiOptions { PageSize = PageSize }).WaitAsync(limit.Token);
            foreach (var installation in installations.Where(i => i.SuspendedAt is null).OrderBy(i => i.Id))
            {
                var token = await app.GitHubApps.CreateInstallationToken(installation.Id).WaitAsync(limit.Token);
                var client = CreateClient(new Credentials(token.Token), transport);
                if (!await HoldsRepositoryAsync(client, repositoryId, limit.Token))
                {
                    continue;
                }

                try
                {
                    return await action(client, limit.Token);
                }
                catch (NotFoundException)
                {
                    return GitHubIssueResult.Failed(GitHubIssueFailure.IssueNotFound);
                }
                catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Gone)
                {
                    return GitHubIssueResult.Failed(GitHubIssueFailure.IssueNotFound);
                }
            }

            return GitHubIssueResult.Failed(GitHubIssueFailure.NotInstalled);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsOutage(exception))
        {
            NoteRateLimit(exception);
            return _limit.ClosedUntil is { } until
                ? GitHubIssueResult.Failed(GitHubIssueFailure.RateLimited, until)
                : GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        }
        catch (ApiException)
        {
            // A host that is stopping is not GitHub refusing: what failed is asked again by the next one.
            cancellationToken.ThrowIfCancellationRequested();
            return GitHubIssueResult.Failed(GitHubIssueFailure.Rejected);
        }
    }
}
