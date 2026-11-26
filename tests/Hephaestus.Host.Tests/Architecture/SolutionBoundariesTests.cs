namespace Hephaestus.Host.Tests.Architecture;

/// <summary>The rules applied to the real solution (AD-3).</summary>
public class SolutionBoundariesTests
{
    private static readonly string[] ExpectedSourceProjects =
    [
        "Hephaestus.Agents",
        "Hephaestus.Core",
        "Hephaestus.GitHub",
        "Hephaestus.Host",
        "Hephaestus.Persistence",
        "Hephaestus.Workspaces",
    ];

    [Fact]
    public void Every_source_project_is_read()
    {
        var projects = ProjectGraphReader.ReadSourceProjects(RepositoryRoot.Path);

        Assert.Equal(ExpectedSourceProjects, projects.Select(p => p.Name));
    }

    [Fact]
    public void Project_references_and_packages_follow_the_boundaries()
    {
        var violations = BoundaryRules.Check(ProjectGraphReader.ReadSourceProjects(RepositoryRoot.Path));

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Host_is_the_only_project_that_references_adapters()
    {
        var projects = ProjectGraphReader.ReadSourceProjects(RepositoryRoot.Path);

        var host = projects.Single(p => p.Name == BoundaryRules.Host);
        Assert.Equal(BoundaryRules.Adapters.Append(BoundaryRules.Core).Order(StringComparer.Ordinal), host.ProjectReferences.Order(StringComparer.Ordinal));
        Assert.All(
            projects.Where(p => BoundaryRules.Adapters.Contains(p.Name)),
            adapter => Assert.Equal([BoundaryRules.Core], adapter.ProjectReferences));
        Assert.Empty(projects.Single(p => p.Name == BoundaryRules.Core).ProjectReferences);
    }
}
