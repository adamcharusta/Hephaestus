using System.Xml.Linq;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>
/// Nullable reference types are on, warnings are errors, and package versions are central (AR-2, AR-3). The rules are set
/// once in <c>Directory.Build.props</c>; no project may weaken them.
/// </summary>
public class BuildRulesTests
{
    private static readonly string[] WeakeningProperties =
    [
        "Nullable",
        "TreatWarningsAsErrors",
        "WarningsAsErrors",
        "NoWarn",
        "WarningsNotAsErrors",
        "TreatSpecificWarningsAsErrors",
        "WarningLevel",
        "AnalysisLevel",
        "ImplicitUsings",
        "LangVersion",
        "TargetFramework",
        "TargetFrameworks",
    ];

    private static readonly Dictionary<string, string> StackPins = new(StringComparer.Ordinal)
    {
        ["Docker.DotNet.Enhanced"] = "4.4.1",
        ["Microsoft.EntityFrameworkCore.Sqlite"] = "10.0.15",
        ["NSubstitute"] = "6.2.0",
        ["Octokit"] = "14.0.0",
        ["System.CommandLine"] = "2.0.15",
        ["Testcontainers"] = "4.17.0",
        ["YamlDotNet"] = "18.1.0",
        ["xunit.v3"] = "4.0.3",
    };

    private static string Root => RepositoryRoot.Path;

    private static IEnumerable<string> ProjectFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "tests"), "*.csproj", SearchOption.AllDirectories));

    private static string Property(string file, string name) =>
        XDocument.Load(file).Descendants(name).Select(e => e.Value.Trim()).LastOrDefault() ?? string.Empty;

    [Fact]
    public void Directory_Build_props_turns_nullable_and_warnings_as_errors_on()
    {
        var props = Path.Combine(Root, "Directory.Build.props");

        Assert.Equal("enable", Property(props, "Nullable"));
        Assert.Equal("true", Property(props, "TreatWarningsAsErrors"));
        Assert.Equal(string.Empty, Property(props, "WarningsAsErrors"));
        Assert.Equal(string.Empty, Property(props, "NoWarn"));
    }

    [Fact]
    public void The_root_Directory_Build_props_is_the_only_place_build_rules_are_set()
    {
        var others = Directory.EnumerateFiles(Root, "Directory.Build.*", SearchOption.AllDirectories)
            .Where(f => f.StartsWith(Path.Combine(Root, "src"), StringComparison.Ordinal) || f.StartsWith(Path.Combine(Root, "tests"), StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Root, f));

        Assert.Empty(others);
        Assert.Equal(string.Empty, Property(Path.Combine(Root, "Directory.Build.targets"), "TreatWarningsAsErrors"));
        Assert.Equal(string.Empty, Property(Path.Combine(Root, "Directory.Build.targets"), "Nullable"));
    }

    [Fact]
    public void No_source_file_turns_nullable_checking_off()
    {
        var files = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "tests"), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.EndsWith("BuildRulesTests.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("#nullable disable", StringComparison.Ordinal) || File.ReadAllText(f).Contains("#pragma warning disable", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Root, f));

        Assert.Empty(files);
    }

    [Fact]
    public void No_project_overrides_a_build_rule()
    {
        var projects = ProjectFiles().ToList();

        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var document = XDocument.Load(project);
            var overridden = WeakeningProperties.Where(p => document.Descendants(p).Any()).ToList();

            Assert.True(overridden.Count == 0, $"{Path.GetFileName(project)} sets {string.Join(", ", overridden)}.");
        }
    }

    [Fact]
    public void Package_versions_live_only_in_Directory_Packages_props()
    {
        Assert.Equal("true", Property(Path.Combine(Root, "Directory.Packages.props"), "ManagePackageVersionsCentrally"));
        foreach (var project in ProjectFiles())
        {
            var document = XDocument.Load(project);
            var versioned = document.Descendants("PackageReference")
                .Where(r => r.Attribute("Version") is not null || r.Attribute("VersionOverride") is not null)
                .Select(r => (string?)r.Attribute("Include") ?? (string?)r.Attribute("Update"));

            Assert.Empty(versioned);
            Assert.Empty(document.Descendants("PackageVersion"));
            Assert.Empty(document.Descendants("GlobalPackageReference"));
        }
    }

    [Fact]
    public void The_pinned_stack_versions_are_the_ones_the_spine_names()
    {
        var versions = XDocument.Load(Path.Combine(Root, "Directory.Packages.props")).Descendants("PackageVersion")
            .ToDictionary(e => (string)e.Attribute("Include")!, e => (string)e.Attribute("Version")!, StringComparer.Ordinal);

        foreach (var (package, version) in StackPins)
        {
            Assert.Equal(version, versions[package]);
        }
    }
}
