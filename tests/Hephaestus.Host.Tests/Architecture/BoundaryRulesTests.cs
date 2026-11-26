namespace Hephaestus.Host.Tests.Architecture;

/// <summary>The rules are checked against modelled graphs, so each failing case is shown to fail.</summary>
public class BoundaryRulesTests
{
    private static List<ProjectNode> Allowed() =>
    [
        ProjectNode.Create(BoundaryRules.Core, packages: ["YamlDotNet"]),
        ProjectNode.Create("Hephaestus.Agents", [BoundaryRules.Core]),
        ProjectNode.Create("Hephaestus.GitHub", [BoundaryRules.Core], ["Octokit"]),
        ProjectNode.Create("Hephaestus.Workspaces", [BoundaryRules.Core], ["Docker.DotNet.Enhanced"]),
        ProjectNode.Create("Hephaestus.Persistence", [BoundaryRules.Core], ["Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Sqlite"]),
        ProjectNode.Create(
            BoundaryRules.Host,
            [BoundaryRules.Core, "Hephaestus.Agents", "Hephaestus.GitHub", "Hephaestus.Workspaces", "Hephaestus.Persistence"],
            ["Octokit", "Docker.DotNet.Enhanced", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore.OpenApi"],
            ["Microsoft.AspNetCore.App"],
            ["Microsoft.AspNetCore.OpenApi"]),
    ];

    private static ProjectNode Replace(List<ProjectNode> graph, ProjectNode replacement)
    {
        graph[graph.FindIndex(p => p.Name == replacement.Name)] = replacement;
        return replacement;
    }

    [Fact]
    public void The_permitted_graph_has_no_violation() =>
        Assert.Empty(BoundaryRules.Check(Allowed()));

    [Theory]
    [InlineData("Hephaestus.Agents")]
    [InlineData("Hephaestus.GitHub")]
    [InlineData("Hephaestus.Workspaces")]
    [InlineData("Hephaestus.Persistence")]
    [InlineData("Hephaestus.Host")]
    public void Core_referencing_a_project_is_a_violation(string target)
    {
        var graph = Allowed();
        Replace(graph, ProjectNode.Create(BoundaryRules.Core, [target]));

        var violation = Assert.Single(BoundaryRules.Check(graph));
        Assert.Contains($"{BoundaryRules.Core} references project {target}", violation);
    }

    [Theory]
    [InlineData("Hephaestus.Agents", "Hephaestus.GitHub")]
    [InlineData("Hephaestus.Persistence", "Hephaestus.Workspaces")]
    [InlineData("Hephaestus.GitHub", BoundaryRules.Host)]
    public void An_adapter_referencing_another_adapter_or_Host_is_a_violation(string adapter, string target)
    {
        var graph = Allowed();
        var current = graph.Single(p => p.Name == adapter);
        Replace(graph, ProjectNode.Create(adapter, current.ProjectReferences.Append(target), current.Packages));

        var violation = Assert.Single(BoundaryRules.Check(graph));
        Assert.Contains($"{adapter} references project {target}", violation);
    }

    [Fact]
    public void A_reference_to_a_project_outside_the_solution_is_a_violation()
    {
        var graph = Allowed();
        Replace(graph, ProjectNode.Create("Hephaestus.Agents", [BoundaryRules.Core, "Elsewhere"]));

        Assert.Contains("Hephaestus.Agents references project Elsewhere", Assert.Single(BoundaryRules.Check(graph)));
    }

    [Theory]
    [InlineData("Octokit")]
    [InlineData("Docker.DotNet.Enhanced")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("Microsoft.AspNetCore.OpenApi")]
    public void Core_resolving_a_vendor_package_is_a_violation_even_when_transitive(string package)
    {
        var graph = Allowed();
        Replace(graph, ProjectNode.Create(BoundaryRules.Core, packages: ["YamlDotNet", "Some.Library", package]));

        var violation = Assert.Single(BoundaryRules.Check(graph));
        Assert.Contains($"{BoundaryRules.Core} resolves package {package}", violation);
    }

    [Fact]
    public void Core_using_the_ASP_NET_Core_framework_is_a_violation()
    {
        var graph = Allowed();
        Replace(graph, ProjectNode.Create(BoundaryRules.Core, frameworkReferences: ["Microsoft.AspNetCore.App"]));

        Assert.Contains("uses framework Microsoft.AspNetCore.App", Assert.Single(BoundaryRules.Check(graph)));
    }

    [Fact]
    public void A_vendor_package_outside_its_owning_adapter_is_a_violation()
    {
        var graph = Allowed();
        Replace(graph, ProjectNode.Create("Hephaestus.Agents", [BoundaryRules.Core], ["Octokit"]));

        var violation = Assert.Single(BoundaryRules.Check(graph));
        Assert.Contains("Hephaestus.Agents resolves package Octokit; only Hephaestus.GitHub may name it", violation);
    }

    [Theory]
    [InlineData("Octokit")]
    [InlineData("Docker.DotNet.Enhanced")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("Microsoft.Data.Sqlite")]
    public void Host_naming_a_vendor_package_itself_is_a_violation_but_resolving_it_through_an_adapter_is_not(string package)
    {
        var graph = Allowed();
        var host = graph.Single(p => p.Name == BoundaryRules.Host);
        Replace(graph, ProjectNode.Create(BoundaryRules.Host, host.ProjectReferences, host.Packages.Append(package), host.FrameworkReferences, ["Microsoft.AspNetCore.OpenApi"]));
        Assert.Empty(BoundaryRules.Check(graph));

        Replace(graph, ProjectNode.Create(BoundaryRules.Host, host.ProjectReferences, host.Packages, host.FrameworkReferences, host.DirectPackages.Append(package)));
        Assert.Contains($"{BoundaryRules.Host} resolves package {package}", Assert.Single(BoundaryRules.Check(graph)));
    }

    [Fact]
    public void Every_violation_is_reported()
    {
        var graph = Allowed();
        Replace(graph, ProjectNode.Create(BoundaryRules.Core, ["Hephaestus.GitHub"], ["Octokit"], ["Microsoft.AspNetCore.App"]));

        Assert.Equal(3, BoundaryRules.Check(graph).Count);
    }

    [Fact]
    public void A_project_outside_the_known_roles_may_reference_nothing()
    {
        var graph = Allowed();
        graph.Add(ProjectNode.Create("Hephaestus.Stray", [BoundaryRules.Core]));

        Assert.Contains("Hephaestus.Stray references project Hephaestus.Core", Assert.Single(BoundaryRules.Check(graph)));
    }
}
