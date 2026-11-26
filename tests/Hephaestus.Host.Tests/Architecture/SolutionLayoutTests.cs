using System.Xml.Linq;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>The solution holds what the spine's source tree lists, and the repository root holds what Story 1.3 requires.</summary>
public class SolutionLayoutTests
{
    private static readonly string[] SourceProjects =
    [
        "Hephaestus.Core",
        "Hephaestus.Agents",
        "Hephaestus.GitHub",
        "Hephaestus.Workspaces",
        "Hephaestus.Persistence",
        "Hephaestus.Host",
    ];

    private static string Root => RepositoryRoot.Path;

    private static List<string> SolutionProjectPaths() =>
        XDocument.Load(Path.Combine(Root, RepositoryRoot.SolutionFile))
            .Descendants("Project")
            .Select(p => (string)p.Attribute("Path")!)
            .ToList();

    [Fact]
    public void The_solution_lists_each_source_project_and_one_test_project_per_source_project_plus_end_to_end()
    {
        var expected = SourceProjects.Select(n => $"src/{n}/{n}.csproj")
            .Concat(SourceProjects.Select(n => $"tests/{n}.Tests/{n}.Tests.csproj"))
            .Append("tests/Hephaestus.EndToEnd.Tests/Hephaestus.EndToEnd.Tests.csproj");

        Assert.Equal(expected.Order(StringComparer.Ordinal), SolutionProjectPaths().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_project_file_under_src_and_tests_is_in_the_solution_and_exists()
    {
        var listed = SolutionProjectPaths().Select(p => Path.GetFullPath(Path.Combine(Root, p))).ToHashSet();
        var onDisk = new[] { "src", "tests" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(Root, folder), "*.csproj", SearchOption.AllDirectories))
            .Select(Path.GetFullPath)
            .ToHashSet();

        Assert.Equal(onDisk.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
        Assert.All(listed, file => Assert.True(File.Exists(file), file));
    }

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("README.md")]
    [InlineData("deploy")]
    [InlineData("methodologies")]
    [InlineData("methodologies/default/manifest.yml")]
    [InlineData("docs")]
    [InlineData("docs/branching.md")]
    [InlineData("Directory.Packages.props")]
    [InlineData("eng/allowed-licenses.txt")]
    [InlineData("eng/coverage-exclusions.txt")]
    [InlineData("eng/check-coverage.py")]
    [InlineData("eng/check-licenses.py")]
    [InlineData("global.json")]
    [InlineData("NuGet.config")]
    [InlineData(".github/workflows/ci.yml")]
    public void The_repository_root_holds(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);

        Assert.True(File.Exists(path) || Directory.Exists(path), relativePath);
    }

    [Fact]
    public void The_license_is_MIT()
    {
        var license = File.ReadAllText(Path.Combine(Root, "LICENSE"));

        Assert.StartsWith("MIT License", license, StringComparison.Ordinal);
        Assert.Contains("Permission is hereby granted, free of charge", license, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_Methodology_is_declared_by_its_manifest()
    {
        var manifest = File.ReadAllLines(Path.Combine(Root, "methodologies", "default", "manifest.yml"));

        Assert.Contains(manifest, line => line.Trim() == "name: default");
    }
}
