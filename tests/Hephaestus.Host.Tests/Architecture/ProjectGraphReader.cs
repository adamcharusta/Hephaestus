using System.Text.Json;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>
/// Reads each source project's restore result (<c>obj/project.assets.json</c>), which lists direct project references,
/// direct packages, framework references, and every package the project resolves, transitive ones included.
/// </summary>
internal static class ProjectGraphReader
{
    public static IReadOnlyList<ProjectNode> ReadSourceProjects(string repositoryRoot)
    {
        var sourceDirectory = Path.Combine(repositoryRoot, "src");
        return Directory.GetDirectories(sourceDirectory)
            .Where(directory => Directory.EnumerateFiles(directory, "*.csproj").Any())
            .Select(directory => Path.GetFileName(directory))
            .Order(StringComparer.Ordinal)
            .Select(name => Read(Path.Combine(sourceDirectory, name, "obj", "project.assets.json"), name))
            .ToList();
    }

    internal static ProjectNode Read(string assetsFile, string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var root = document.RootElement;
        var project = root.GetProperty("project");

        var projectReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var framework in project.GetProperty("restore").GetProperty("frameworks").EnumerateObject())
        {
            foreach (var reference in framework.Value.GetProperty("projectReferences").EnumerateObject())
            {
                projectReferences.Add(Path.GetFileNameWithoutExtension(reference.Name));
            }
        }

        var frameworkReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var framework in project.GetProperty("frameworks").EnumerateObject())
        {
            frameworkReferences.UnionWith(Names(framework.Value, "frameworkReferences"));
        }

        var directPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in root.GetProperty("projectFileDependencyGroups").EnumerateObject())
        {
            // Each entry reads "Package >= 1.2.3"; project references appear too and are not packages.
            directPackages.UnionWith(group.Value.EnumerateArray().Select(entry => entry.GetString()!.Split(' ')[0]));
        }

        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in root.GetProperty("targets").EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject().Where(l => l.Value.GetProperty("type").GetString() == "package"))
            {
                packages.Add(library.Name[..library.Name.IndexOf('/', StringComparison.Ordinal)]);
                frameworkReferences.UnionWith(Names(library.Value, "frameworkReferences"));
            }
        }

        directPackages.IntersectWith(packages);
        return new ProjectNode(name, projectReferences, packages, directPackages, frameworkReferences);
    }

    private static IEnumerable<string> Names(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(v => v.GetString()!)
            : element.TryGetProperty(property, out var map) && map.ValueKind == JsonValueKind.Object
                ? map.EnumerateObject().Select(p => p.Name)
                : [];
}
