namespace Hephaestus.Host.Tests.Architecture;

/// <summary>The reader is run on a small restore result of known shape, so that it cannot silently stop reading a part of it.</summary>
public sealed class ProjectGraphReaderTests : IDisposable
{
    private const string Assets = """
        {
          "version": 3,
          "targets": {
            "net10.0": {
              "Direct.Package/1.0.0": { "type": "package" },
              "Transitive.Package/2.0.0": { "type": "package", "frameworkReferences": ["Microsoft.AspNetCore.App"] },
              "Hephaestus.Core/1.0.0": { "type": "project" }
            }
          },
          "libraries": {},
          "projectFileDependencyGroups": {
            "net10.0": [ "Direct.Package >= 1.0.0", "Hephaestus.Core >= 1.0.0" ]
          },
          "project": {
            "restore": {
              "frameworks": {
                "net10.0": {
                  "projectReferences": { "/repo/src/Hephaestus.Core/Hephaestus.Core.csproj": { "projectPath": "/repo/src/Hephaestus.Core/Hephaestus.Core.csproj" } }
                }
              }
            },
            "frameworks": {
              "net10.0": { "frameworkReferences": { "Microsoft.NETCore.App": { "privateAssets": "all" } } }
            }
          }
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hephaestus-graph-{Guid.NewGuid():N}");

    public ProjectGraphReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string path, string content)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public void Project_references_direct_and_transitive_packages_and_frameworks_are_all_read()
    {
        var node = ProjectGraphReader.Read(Write("a/obj/project.assets.json", Assets), "Hephaestus.Agents");

        Assert.Equal("Hephaestus.Agents", node.Name);
        Assert.Equal(["Hephaestus.Core"], node.ProjectReferences);
        Assert.Equal(["Direct.Package", "Transitive.Package"], node.Packages.Order(StringComparer.Ordinal));
        Assert.Equal(["Direct.Package"], node.DirectPackages);
        Assert.Equal(["Microsoft.AspNetCore.App", "Microsoft.NETCore.App"], node.FrameworkReferences.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_framework_reference_a_package_brings_in_reaches_the_rules()
    {
        var node = ProjectGraphReader.Read(Write("a/obj/project.assets.json", Assets), BoundaryRules.Core);

        Assert.Contains("uses framework Microsoft.AspNetCore.App", BoundaryRules.Check([node]).Single(v => v.Contains("framework", StringComparison.Ordinal)));
    }

    [Fact]
    public void Every_target_framework_is_read()
    {
        var multi = Assets.Replace("\"net10.0\": { \"frameworkReferences\"", "\"net9.0\": { \"frameworkReferences\": { \"Microsoft.WindowsDesktop.App\": {} } }, \"net10.0\": { \"frameworkReferences\"", StringComparison.Ordinal);

        var node = ProjectGraphReader.Read(Write("a/obj/project.assets.json", multi), "Hephaestus.Agents");

        Assert.Contains("Microsoft.WindowsDesktop.App", node.FrameworkReferences);
    }

    [Fact]
    public void Only_directories_with_a_project_file_are_source_projects()
    {
        Write("src/Hephaestus.One/Hephaestus.One.csproj", "<Project />");
        Write("src/Hephaestus.One/obj/project.assets.json", Assets);
        Write("src/Hephaestus.Dashboard/package.json", "{}");

        var projects = ProjectGraphReader.ReadSourceProjects(_root);

        Assert.Equal(["Hephaestus.One"], projects.Select(p => p.Name));
    }
}
