using Hephaestus.Core.Aliases;
using Hephaestus.Core.Methodologies;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// The Methodologies the image ships (AD-14): each subdirectory of the <c>methodologies</c> directory next to the executable that holds a
/// <c>manifest.yml</c> and is named like an Alias is one. A directory without a manifest is a place kept for a Methodology that is not there yet,
/// so it is not available.
/// </summary>
internal sealed class DirectoryMethodologyCatalog(string directory) : IMethodologyCatalog
{
    public const string ManifestName = "manifest.yml";

    /// <summary>The most files, and the most bytes in all of them, that one Methodology may hold; the image ships far less, and what is read here is placed in a volume for every step.</summary>
    public const int MaximumFiles = 2000;

    public const long MaximumBytes = 32 * 1024 * 1024;

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "methodologies");

    public IReadOnlyList<string> ListNames()
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return [.. Directory.EnumerateDirectories(directory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => AliasShape.IsValidName(name) && File.Exists(Path.Combine(directory, name, ManifestName)))
            .Order(StringComparer.Ordinal)];
    }

    public async Task<Methodology?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        if (!AliasShape.IsValidName(name) || !ListNames().Contains(name))
        {
            return null;
        }

        var root = Path.Combine(directory, name);
        var files = new List<WorkspaceFile>();
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null)
            {
                continue;
            }

            total += info.Length;
            if (files.Count >= MaximumFiles || total > MaximumBytes)
            {
                throw new InvalidOperationException($"The Methodology {name} holds more than {MaximumFiles} files or {MaximumBytes} bytes.");
            }

            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            files.Add(new WorkspaceFile(relative, await File.ReadAllBytesAsync(path, cancellationToken)));
        }

        return new Methodology(name, files);
    }
}
