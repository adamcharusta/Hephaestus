using Hephaestus.Core.Aliases;
using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// The Methodologies the image ships (AD-14): each subdirectory of the <c>methodologies</c> directory next to the executable that holds a
/// <c>manifest.yml</c> and is named like an Alias is one. A directory without a manifest is a place kept for a Methodology that is not there yet,
/// so it is not available.
/// </summary>
internal sealed class DirectoryMethodologyCatalog(string directory) : IMethodologyCatalog
{
    public const string ManifestName = "manifest.yml";

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
}
