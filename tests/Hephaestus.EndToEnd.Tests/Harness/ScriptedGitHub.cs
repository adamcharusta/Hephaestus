using System.Text;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.EndToEnd.Tests.Harness;

/// <summary>
/// GitHub as a Task meets it from labelled issue to pull request, in memory (Story 3.14, AR-37): the file of the Project on its Base Branch, the labels of its issues, the mirror that bundles a commit, the branch a checkpoint is pushed to, and the pull
/// request that is opened. It is one object because the harness's GitHub is: the installation under test is real, and everything beyond its adapters is scripted.
/// </summary>
internal sealed class ScriptedGitHub : ICodeHost, IRepositoryMirrors, ITaskBranches, IPullRequests
{
    public const string BaseCommit = "0123456789abcdef0123456789abcdef01234567";

    private readonly object _gate = new();
    private readonly Dictionary<(long Repository, int Issue), List<string>> _labels = [];
    private readonly Dictionary<string, (int Number, PullRequestPublication Last)> _pulls = [];

    /// <summary>The text of <c>hephaestus.yml</c> on the Base Branch.</summary>
    public string Configuration { get; set; } = string.Empty;

    public List<string> Pushed { get; } = [];

    public List<PullRequestPublication> Publications { get; } = [];

    public List<IReadOnlyList<string>> LabelHistory { get; } = [];

    /// <summary>The comments posted on issues, as the text that was posted.</summary>
    public List<(int Issue, string Text)> Comments { get; } = [];

    public void Opted(long repository, int issue) => _labels[(repository, issue)] = ["hephaestus"];

    public IReadOnlyList<string> LabelsOf(long repository, int issue)
    {
        lock (_gate)
        {
            return [.. _labels.GetValueOrDefault((repository, issue)) ?? []];
        }
    }

    public int OpenPullRequests(string head) => _pulls.ContainsKey(head) ? 1 : 0;

    public Task<GitHubFileResult> ReadFileAsync(long repositoryId, string branch, string path, int maximumLength, CancellationToken cancellationToken) =>
        Task.FromResult(GitHubFileResult.Read(new GitHubFile(Encoding.UTF8.GetBytes(Configuration), BaseCommit)));

    public Task<GitHubIssueResult> ReadIssueLabelsAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_labels.TryGetValue((repositoryId, issueNumber), out var labels) ? GitHubIssueResult.Labelled([.. labels]) : GitHubIssueResult.Failed(GitHubIssueFailure.IssueNotFound));
        }
    }

    public Task<GitHubIssueResult> ChangeIssueLabelsAsync(long repositoryId, int issueNumber, IReadOnlyList<string> add, IReadOnlyList<string> remove, string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_labels.TryGetValue((repositoryId, issueNumber), out var labels))
            {
                return Task.FromResult(GitHubIssueResult.Failed(GitHubIssueFailure.IssueNotFound));
            }

            labels.RemoveAll(remove.Contains);
            labels.AddRange(add.Where(label => !labels.Contains(label)));
            LabelHistory.Add([.. labels]);
            return Task.FromResult(GitHubIssueResult.Labelled([.. labels]));
        }
    }

    public Task<GitHubCommentResult> FindIssueCommentAsync(long repositoryId, int issueNumber, string idempotencyKey, CancellationToken cancellationToken) => Task.FromResult(GitHubCommentResult.Absent);

    public Task<GitHubCommentResult> PostIssueCommentAsync(long repositoryId, int issueNumber, string text, string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Comments.Add((issueNumber, text));
        }

        return Task.FromResult(GitHubCommentResult.Present(1));
    }

    public Task<GitHubIdentityResult> ConnectIdentityAsync(long appId, string privateKey, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GitHubIdentityResult> CheckIdentityAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GitHubUserResult> FindUserAsync(string login, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GitHubRepositoryResult> FindRepositoryAsync(string fullName, string? branch, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<MirrorResult> FetchAsync(long repositoryId, CancellationToken cancellationToken) => Task.FromResult(MirrorResult.Fetched);

    public Task<BundleResult> CreateBundleAsync(BundleRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(BundleResult.Made(new BundleReference($"{request.TaskId:D}/{request.StepRunId:D}.bundle", new string('d', 64), request.Commit)));

    public Task<BranchCheckResult> CheckBranchAsync(BranchCheckRequest request, CancellationToken cancellationToken) => Task.FromResult(BranchCheckResult.InSync());

    public Task<PushResult> PushAsync(PushRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Pushed.Add($"{request.Branch}@{request.Tip}");
        }

        return Task.FromResult(PushResult.Pushed());
    }

    public void DeleteBundle(BundleReference bundle)
    {
    }

    public Task<PullRequestResult> PublishAsync(PullRequestPublication request, string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Publications.Add(request);
            if (_pulls.TryGetValue(request.Head, out var existing))
            {
                _pulls[request.Head] = (existing.Number, request);
                return Task.FromResult(PullRequestResult.Updated(existing.Number));
            }

            _pulls[request.Head] = (40 + _pulls.Count, request);
            return Task.FromResult(PullRequestResult.Opened(40 + _pulls.Count - 1));
        }
    }
}
