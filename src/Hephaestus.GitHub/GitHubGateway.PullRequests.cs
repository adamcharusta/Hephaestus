using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Octokit;

namespace Hephaestus.GitHub;

/// <summary>
/// The gateway's work on the pull request of a Task (AD-4, AD-5, FR-36): it is opened, or, when its branch has one already, updated, under the identity of the App's installation, which is Hephaestus's own. The gateway has no call that merges,
/// closes, or reviews one, and opens one only from a Task branch, which the request already guarantees (FR-44).
/// </summary>
public sealed partial class GitHubGateway : IPullRequests
{
    public async Task<PullRequestResult> PublishAsync(PullRequestPublication request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        // The values are read again, so that a secret stored a moment ago is masked too (FR-4).
        await _masker.RebuildAsync(cancellationToken);
        var title = _masker.Mask(request.Title);
        var body = _masker.Mask(request.Body);
        return await OnIssueAsync(
            request.RepositoryId,
            async (client, token) =>
            {
                // The open pull request of the head branch is looked for first, so that one made before a crash and not recorded is found and never made twice. The head is qualified by the owner, as GitHub's filter wants.
                var repository = await client.Repository.Get(request.RepositoryId).WaitAsync(token);
                var filter = new Octokit.PullRequestRequest { State = ItemStateFilter.Open, Head = repository.Owner.Login + ":" + request.Head };
                var open = await client.PullRequest.GetAllForRepository(request.RepositoryId, filter, new ApiOptions { PageSize = PageSize }).WaitAsync(token);
                var existing = open.FirstOrDefault(pull => string.Equals(pull.Head.Ref, request.Head, StringComparison.Ordinal));
                if (existing is not null)
                {
                    await client.PullRequest.Update(request.RepositoryId, existing.Number, new PullRequestUpdate { Title = title, Body = body }).WaitAsync(token);
                    return PullRequestResult.Updated(existing.Number);
                }

                var created = await client.PullRequest.Create(request.RepositoryId, new NewPullRequest(title, request.Head, request.Base) { Body = body }).WaitAsync(token);
                return PullRequestResult.Opened(created.Number);
            },
            PullRequestResult.Failed,
            cancellationToken);
    }
}
