using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// GitHub as the loop asks it, in memory: the labels of issues. An issue nobody set up has the Marker Label and nothing else, as one that
/// was just opted in; <see cref="Missing"/> names those that do not exist. A test makes it fail or hang as GitHub does.
/// </summary>
internal sealed class FakeIssues : ICodeHost
{
    private readonly Dictionary<(long RepositoryId, int IssueNumber), List<string>> _labels = [];

    public HashSet<(long RepositoryId, int IssueNumber)> Missing { get; } = [];

    /// <summary>What every read and change answers instead, while set.</summary>
    public GitHubIssueResult? Answer { get; set; }

    /// <summary>What a read answers, in order, before it answers as it should; one per call.</summary>
    public Queue<GitHubIssueResult> ReadAnswers { get; } = [];

    /// <summary>What a change answers, in order, before it answers as it should; one per call.</summary>
    public Queue<GitHubIssueResult> ChangeAnswers { get; } = [];

    /// <summary>When set, a read or a change throws it.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Runs just before a change reaches the labels, with the key of the effect; a test makes the change and then loses the answer.</summary>
    public Action<string>? BeforeChange { get; set; }

    public int Reads { get; private set; }

    public List<(long RepositoryId, int IssueNumber, string[] Add, string[] Remove, string Key)> Changes { get; } = [];

    public List<string> LabelsOf(long repositoryId, int issueNumber) => Of(repositoryId, issueNumber);

    private List<string> Of(long repositoryId, int issueNumber)
    {
        if (!_labels.TryGetValue((repositoryId, issueNumber), out var labels))
        {
            labels = [Labels.Marker];
            _labels[(repositoryId, issueNumber)] = labels;
        }

        return labels;
    }

    public Task<GitHubIssueResult> ReadIssueLabelsAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken)
    {
        Reads++;
        if (Throws is not null)
        {
            throw Throws;
        }

        if (ReadAnswers.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted);
        }

        if (Answer is not null)
        {
            return Task.FromResult(Answer);
        }

        return Task.FromResult(Missing.Contains((repositoryId, issueNumber))
            ? GitHubIssueResult.Failed(GitHubIssueFailure.IssueNotFound)
            : GitHubIssueResult.Labelled([.. Of(repositoryId, issueNumber)]));
    }

    public Task<GitHubIssueResult> ChangeIssueLabelsAsync(
        long repositoryId,
        int issueNumber,
        IReadOnlyList<string> add,
        IReadOnlyList<string> remove,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Throws is not null)
        {
            throw Throws;
        }

        BeforeChange?.Invoke(idempotencyKey);
        Changes.Add((repositoryId, issueNumber, [.. add], [.. remove], idempotencyKey));
        if (Missing.Contains((repositoryId, issueNumber)))
        {
            return Task.FromResult(GitHubIssueResult.Failed(GitHubIssueFailure.IssueNotFound));
        }

        var labels = Of(repositoryId, issueNumber);
        labels.RemoveAll(label => remove.Contains(label, StringComparer.OrdinalIgnoreCase));
        labels.AddRange(add.Where(label => !labels.Contains(label, StringComparer.OrdinalIgnoreCase)));
        if (ChangeAnswers.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted);
        }

        return Task.FromResult(Answer ?? GitHubIssueResult.Labelled([.. labels]));
    }

    public Task<GitHubIdentityResult> ConnectIdentityAsync(long appId, string privateKey, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GitHubIdentityResult> CheckIdentityAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GitHubUserResult> FindUserAsync(string login, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GitHubRepositoryResult> FindRepositoryAsync(string fullName, string? branch, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GitHubFileResult> ReadFileAsync(long repositoryId, string branch, string path, int maximumLength, CancellationToken cancellationToken) => throw new NotSupportedException();
}
