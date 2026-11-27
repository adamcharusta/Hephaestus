using System.Globalization;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Projects;

/// <summary>What the check found in the file.</summary>
public enum ConfigurationStatus
{
    /// <summary>The file is there and every setting is valid.</summary>
    Valid,

    /// <summary>The file is there and at least one thing is wrong with it.</summary>
    Invalid,

    /// <summary>The Base Branch has no <c>hephaestus.yml</c> at its root.</summary>
    Missing,
}

/// <summary>The outcome of checking a Project's configuration file.</summary>
/// <param name="Failure">Why no check happened: the Project was not found, or GitHub could not be read. <see cref="ProjectFailure.None"/> when the file was looked at.</param>
/// <param name="Project">The Project that was checked, when it was found.</param>
/// <param name="CommitId">The tip of the Base Branch the file was read from.</param>
/// <param name="Configuration">The settings of a valid file.</param>
/// <param name="Issues">Every problem of an invalid file, in the order of the file.</param>
public sealed record ConfigurationCheck(
    ProjectFailure Failure,
    Project? Project,
    ConfigurationStatus? Status,
    string? CommitId,
    ProjectConfiguration? Configuration,
    IReadOnlyList<ConfigurationIssue> Issues)
{
    public static ConfigurationCheck Failed(ProjectFailure failure, Project? project = null) => new(failure, project, null, null, null, []);
}

/// <summary>
/// Checks a Project's <c>hephaestus.yml</c> (FR-2, AD-16): the gateway returns the raw bytes of the file at the tip of the Base Branch with that
/// commit's id, and everything else happens here. Nothing is stored; the Aliases, limits, and Methodologies it is checked against are the
/// installation's now.
/// </summary>
public sealed class ProjectConfigurationService(ProjectService projects, ICodeHost codeHost, IReadStore reads, IMethodologyCatalog methodologies)
{
    public async Task<ConfigurationCheck> CheckAsync(string fullName, CancellationToken cancellationToken)
    {
        var (project, failure) = await projects.FindAsync(fullName, cancellationToken);
        if (project is null)
        {
            return ConfigurationCheck.Failed(failure);
        }

        var read = await codeHost.ReadFileAsync(project.RepositoryId, project.BaseBranch, ProjectConfigurationFile.Path, ProjectConfigurationFile.MaximumLength, cancellationToken);
        switch (read.Failure)
        {
            case GitHubFileFailure.None when read.File is { } file:
                var context = new ConfigurationContext(await reads.ListAliasesAsync(cancellationToken), await reads.GetLimitsAsync(cancellationToken), methodologies.ListNames());
                var parse = ProjectConfigurationParser.Parse(file.Content, context);
                return new ConfigurationCheck(
                    ProjectFailure.None,
                    project,
                    parse.IsValid ? ConfigurationStatus.Valid : ConfigurationStatus.Invalid,
                    file.CommitId,
                    parse.Configuration,
                    parse.Issues);
            case GitHubFileFailure.NotFound:
                return new ConfigurationCheck(ProjectFailure.None, project, ConfigurationStatus.Missing, read.CommitId, null, []);
            case GitHubFileFailure.TooLarge:
                var tooLarge = new ConfigurationIssue(ProjectConfigurationFile.Path, null, new Message(MessageIds.ConfigIssueTooLarge, ProjectConfigurationFile.MaximumLength.ToString(CultureInfo.InvariantCulture)));
                return new ConfigurationCheck(ProjectFailure.None, project, ConfigurationStatus.Invalid, read.CommitId, null, [tooLarge]);
            default:
                return ConfigurationCheck.Failed(Map(read.Failure), project);
        }
    }

    private static ProjectFailure Map(GitHubFileFailure failure) => failure switch
    {
        GitHubFileFailure.NotInstalled => ProjectFailure.NotInstalled,
        GitHubFileFailure.BranchNotFound => ProjectFailure.BranchNotFound,
        GitHubFileFailure.NotConnected => ProjectFailure.NotConnected,
        GitHubFileFailure.KeyUnreadable => ProjectFailure.KeyUnreadable,
        GitHubFileFailure.Rejected => ProjectFailure.Rejected,
        _ => ProjectFailure.Unreachable,
    };
}
