using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// The pull requests of Tasks on GitHub as the loop asks them, in memory, with the rule of the gateway: the branch has at most one open pull request, so a publication for a branch that has one updates it and does not open another.
/// </summary>
internal sealed class FakePullRequests : IPullRequests
{
    private readonly Dictionary<string, (int Number, PullRequestPublication Last)> _open = [];

    /// <summary>What a publication answers, in order, before it answers as it should; one per call.</summary>
    public Queue<PullRequestResult> Answers { get; } = [];

    /// <summary>When set, a publication throws it.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Runs just before a publication reaches the pull requests, with the key of the effect; a test publishes and then loses the answer.</summary>
    public Action<string>? BeforePublish { get; set; }

    public List<(PullRequestPublication Request, string Key)> Publications { get; } = [];

    /// <summary>Makes the branch have an open pull request already, as one opened before a crash.</summary>
    public void Seed(string head, int number) => _open[head] = (number, new PullRequestPublication(1, head, "main", "earlier", string.Empty));

    /// <summary>The number of open pull requests of a branch: none or one.</summary>
    public int OpenOf(string head) => _open.ContainsKey(head) ? 1 : 0;

    public (int Number, PullRequestPublication Last) Pull(string head) => _open[head];

    public Task<PullRequestResult> PublishAsync(PullRequestPublication request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (Throws is not null)
        {
            return Task.FromException<PullRequestResult>(Throws);
        }

        BeforePublish?.Invoke(idempotencyKey);
        Publications.Add((request, idempotencyKey));
        if (Answers.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted);
        }

        if (_open.TryGetValue(request.Head, out var existing))
        {
            _open[request.Head] = (existing.Number, request);
            return Task.FromResult(PullRequestResult.Updated(existing.Number));
        }

        var number = _open.Count + 40;
        _open[request.Head] = (number, request);
        return Task.FromResult(PullRequestResult.Opened(number));
    }
}
