using System.Net;
using Hephaestus.Core.Effects;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Trust;
using Octokit;
using Octokit.Internal;

namespace Hephaestus.GitHub;

/// <summary>
/// The gateway's work on an issue (AD-4, AD-5): a read of its labels, and a change that adds and removes labels as a set operation (Story 2.4);
/// and the finding and the posting of a comment by its hidden marker (Story 2.5). What is wanted is decided by the caller; the gateway only
/// asks GitHub, and tells a wait or an outage, which are tried again, from a refusal, which is for good.
/// </summary>
public sealed partial class GitHubGateway
{
    public Task<GitHubIssueResult> ReadIssueLabelsAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken) =>
        OnIssueAsync(repositoryId, async (client, token) => GitHubIssueResult.Labelled(await LabelsOfAsync(client, repositoryId, issueNumber, token)), GitHubIssueResult.Failed, cancellationToken);

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
            GitHubIssueResult.Failed,
            cancellationToken);
    }

    public async Task<GitHubCommentResult> FindIssueCommentAsync(long repositoryId, int issueNumber, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        // A marker is believed only in a comment that Hephaestus's own bot account wrote: anyone can write the text of one. Without the App's
        // slug the account cannot be told, and a comment would be taken for missing and posted twice, so nothing is answered until it is known.
        var slug = await _reads.GetGitHubAppSlugAsync(cancellationToken);
        if (slug is null)
        {
            // An App connected before the slug was kept: reading the identity stores it, as `github check` does.
            var identity = await CheckIdentityAsync(cancellationToken);
            if (identity.Identity is not { } read)
            {
                return GitHubCommentResult.Failed(identity.Failure switch
                {
                    GitHubIdentityFailure.KeyUnreadable => GitHubIssueFailure.KeyUnreadable,
                    GitHubIdentityFailure.Rejected => GitHubIssueFailure.Rejected,
                    GitHubIdentityFailure.Unreachable => GitHubIssueFailure.Unreachable,
                    _ => GitHubIssueFailure.NotConnected,
                });
            }

            slug = read.Slug;
        }

        var own = GitHubLogin.BotOf(slug);
        return await OnIssueAsync(
            repositoryId,
            async (client, token) =>
            {
                var comments = await client.Issue.Comment.GetAllForIssue(repositoryId, issueNumber, new ApiOptions { PageSize = PageSize }).WaitAsync(token);
                var found = comments.FirstOrDefault(comment => comment.User is { } author && GitHubLogin.SameLogin(author.Login, own) && EffectMarkers.EndsWith(comment.Body, idempotencyKey));
                return found is null ? GitHubCommentResult.Absent : GitHubCommentResult.Present(found.Id);
            },
            GitHubCommentResult.Failed,
            cancellationToken);
    }

    public async Task<GitHubCommentResult> PostIssueCommentAsync(long repositoryId, int issueNumber, string text, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        // The values are read again, so that a secret stored a moment ago is masked too, and the marker is added after the masking, as the
        // last line, so that it is what the comment ends with whatever the text held.
        await _masker.RebuildAsync(cancellationToken);
        var body = EffectMarkers.Append(_masker.Mask(text), idempotencyKey);
        return await OnIssueAsync(
            repositoryId,
            async (client, token) => GitHubCommentResult.Present((await client.Issue.Comment.Create(repositoryId, issueNumber, body).WaitAsync(token)).Id),
            GitHubCommentResult.Failed,
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
    private async Task<T> OnIssueAsync<T>(
        long repositoryId,
        Func<GitHubClient, CancellationToken, Task<T>> action,
        Func<GitHubIssueFailure, DateTimeOffset?, T> failed,
        CancellationToken cancellationToken)
    {
        if (_limit.ClosedUntil is { } closed)
        {
            return failed(GitHubIssueFailure.RateLimited, closed);
        }

        var (appId, privateKey) = await ReadCredentialAsync(cancellationToken);
        if (appId is null || privateKey is null)
        {
            return failed(GitHubIssueFailure.NotConnected, null);
        }

        var jwt = GitHubAppJwt.TryCreate(appId.Value, privateKey, _time.GetUtcNow());
        if (jwt is null)
        {
            return failed(GitHubIssueFailure.KeyUnreadable, null);
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
                    return failed(GitHubIssueFailure.IssueNotFound, null);
                }
                catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Gone)
                {
                    return failed(GitHubIssueFailure.IssueNotFound, null);
                }
            }

            return failed(GitHubIssueFailure.NotInstalled, null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsOutage(exception))
        {
            NoteRateLimit(exception);
            return _limit.ClosedUntil is { } until
                ? failed(GitHubIssueFailure.RateLimited, until)
                : failed(GitHubIssueFailure.Unreachable, null);
        }
        catch (ApiException)
        {
            // A host that is stopping is not GitHub refusing: what failed is asked again by the next one.
            cancellationToken.ThrowIfCancellationRequested();
            return failed(GitHubIssueFailure.Rejected, null);
        }
    }
}
