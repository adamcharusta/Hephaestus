using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.GitHub;

/// <summary>
/// A bundle of one commit of a Project's mirror, for the checkout of one step (AD-9). <see cref="Commit"/> is the Task's last pushed tip, or its start commit on the Base Branch
/// when nothing was pushed yet; the file is named after the step run, so that a retry of the step makes the same file.
/// </summary>
public sealed record BundleRequest
{
    public BundleRequest(long repositoryId, string commit, Guid taskId, Guid stepRunId)
    {
        if (!BundleReference.IsCommitId(commit))
        {
            throw new ArgumentException("A bundle is made of a commit id in 40 lower-case hexadecimal digits.", nameof(commit));
        }

        RepositoryId = repositoryId;
        Commit = commit;
        TaskId = taskId;
        StepRunId = stepRunId;
    }

    public long RepositoryId { get; }

    public string Commit { get; }

    public Guid TaskId { get; }

    public Guid StepRunId { get; }
}
