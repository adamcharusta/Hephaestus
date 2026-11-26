using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Projects;

/// <summary>Why a change to the Projects did not happen.</summary>
public enum ProjectFailure
{
    /// <summary>It happened.</summary>
    None,

    /// <summary>The name is not <c>owner/repo</c>; GitHub was not asked.</summary>
    InvalidName,

    /// <summary>The Base Branch is not shaped like a branch name; GitHub was not asked.</summary>
    InvalidBranch,

    /// <summary>The GitHub App is not installed on the repository, or the repository does not exist.</summary>
    NotInstalled,

    /// <summary>The repository has no such branch.</summary>
    BranchNotFound,

    /// <summary>The name is not that of a registered Project.</summary>
    NotRegistered,

    /// <summary>No GitHub App is connected, so a repository cannot be found.</summary>
    NotConnected,

    /// <summary>The stored App key cannot be read.</summary>
    KeyUnreadable,

    /// <summary>GitHub refused the stored credential.</summary>
    Rejected,

    /// <summary>GitHub did not answer.</summary>
    Unreachable,
}

/// <summary>The outcome of registering or removing a Project.</summary>
/// <param name="Project">The Project as it is now stored; for a removal, as it was removed.</param>
/// <param name="Change">What registering did; null for a removal.</param>
/// <param name="BaseBranchProtected">Whether GitHub reports the Base Branch as protected; null for a removal.</param>
public sealed record ProjectResult(ProjectFailure Failure, Project? Project, ProjectChange? Change, bool? BaseBranchProtected)
{
    public static ProjectResult Failed(ProjectFailure failure) => new(failure, null, null, null);

    public static ProjectResult Registered(ProjectWrite write, bool baseBranchProtected) => new(ProjectFailure.None, write.Project, write.Change, baseBranchProtected);

    public static ProjectResult Removed(Project project) => new(ProjectFailure.None, project, null, null);
}

/// <summary>
/// The rules for Projects (FR-1, AD-19): a Project is a repository the GitHub App is installed on, identified by GitHub's repository
/// id, with a Base Branch that exists. Removing a Project retires it and deletes nothing.
/// </summary>
public sealed class ProjectService(ICodeHost codeHost, IReadStore reads, IInstallationConfiguration configuration)
{
    public async Task<ProjectResult> AddAsync(string fullName, string? baseBranch, CancellationToken cancellationToken)
    {
        if (!ProjectName.IsValid(fullName))
        {
            return ProjectResult.Failed(ProjectFailure.InvalidName);
        }

        if (!ProjectName.IsValidBranch(baseBranch))
        {
            return ProjectResult.Failed(ProjectFailure.InvalidBranch);
        }

        var lookup = await codeHost.FindRepositoryAsync(fullName, baseBranch, cancellationToken);
        if (lookup.Repository is not { Branch: { } branch } repository)
        {
            return ProjectResult.Failed(Map(lookup.Failure));
        }

        var write = await configuration.RegisterProjectAsync(repository.Id, repository.FullName, branch.Name, cancellationToken);
        return ProjectResult.Registered(write, branch.IsProtected);
    }

    public async Task<ProjectResult> RemoveAsync(string fullName, CancellationToken cancellationToken)
    {
        if (!ProjectName.IsValid(fullName))
        {
            return ProjectResult.Failed(ProjectFailure.InvalidName);
        }

        // What the Owner sees in the list is what is removed, and retiring a Project does not depend on GitHub answering.
        var projects = await reads.ListProjectsAsync(includeRemoved: false, cancellationToken);
        var byName = projects.Where(p => ProjectName.SameName(p.FullName, fullName)).ToList();
        var target = byName.Count == 1 ? byName[0] : null;
        if (target is null)
        {
            if (projects.Count == 0)
            {
                return ProjectResult.Failed(ProjectFailure.NotRegistered);
            }

            // No stored name matches, or several do (a stale one beside the current one): GitHub says which repository the name is now,
            // so a repository that was renamed is found by the name it has today.
            var lookup = await codeHost.FindRepositoryAsync(fullName, branch: null, cancellationToken);
            if (lookup.Repository is not { } found)
            {
                return ProjectResult.Failed(lookup.Failure == GitHubRepositoryFailure.NotInstalled ? ProjectFailure.NotRegistered : Map(lookup.Failure));
            }

            target = projects.FirstOrDefault(p => p.RepositoryId == found.Id);
            if (target is null)
            {
                return ProjectResult.Failed(ProjectFailure.NotRegistered);
            }
        }

        var removed = await configuration.RemoveProjectAsync(target.RepositoryId, cancellationToken);
        return removed is null ? ProjectResult.Failed(ProjectFailure.NotRegistered) : ProjectResult.Removed(removed);
    }

    private static ProjectFailure Map(GitHubRepositoryFailure failure) => failure switch
    {
        GitHubRepositoryFailure.NotInstalled => ProjectFailure.NotInstalled,
        GitHubRepositoryFailure.BranchNotFound => ProjectFailure.BranchNotFound,
        GitHubRepositoryFailure.NotConnected => ProjectFailure.NotConnected,
        GitHubRepositoryFailure.KeyUnreadable => ProjectFailure.KeyUnreadable,
        GitHubRepositoryFailure.Rejected => ProjectFailure.Rejected,
        _ => ProjectFailure.Unreachable,
    };
}
