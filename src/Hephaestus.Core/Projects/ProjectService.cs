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
        var (project, failure) = await FindAsync(fullName, cancellationToken);
        if (project is null)
        {
            return ProjectResult.Failed(failure);
        }

        var removed = await configuration.RemoveProjectAsync(project.RepositoryId, cancellationToken);
        return removed is null ? ProjectResult.Failed(ProjectFailure.NotRegistered) : ProjectResult.Removed(removed);
    }

    /// <summary>
    /// The Project the Owner means by <paramref name="fullName"/>, among those Hephaestus works on: what the list shows, by its stored name, so that
    /// acting on a Project does not depend on GitHub answering. Only when no stored name matches, or several do (a stale name beside the current
    /// one), GitHub says which repository the name is now, so a repository that was renamed is found by the name it has today.
    /// </summary>
    public async Task<(Project? Project, ProjectFailure Failure)> FindAsync(string fullName, CancellationToken cancellationToken)
    {
        if (!ProjectName.IsValid(fullName))
        {
            return (null, ProjectFailure.InvalidName);
        }

        var projects = await reads.ListProjectsAsync(includeRemoved: false, cancellationToken);
        var byName = projects.Where(p => ProjectName.SameName(p.FullName, fullName)).ToList();
        if (byName.Count == 1)
        {
            return (byName[0], ProjectFailure.None);
        }

        if (projects.Count == 0)
        {
            return (null, ProjectFailure.NotRegistered);
        }

        var lookup = await codeHost.FindRepositoryAsync(fullName, branch: null, cancellationToken);
        if (lookup.Repository is not { } found)
        {
            return (null, lookup.Failure == GitHubRepositoryFailure.NotInstalled ? ProjectFailure.NotRegistered : Map(lookup.Failure));
        }

        var target = projects.FirstOrDefault(p => p.RepositoryId == found.Id);
        return target is null ? (null, ProjectFailure.NotRegistered) : (target, ProjectFailure.None);
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
