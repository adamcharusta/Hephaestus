using Hephaestus.Core.Methodologies;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The Methodologies this Hephaestus ships (AD-14): a directory with a manifest. Project Configuration may select only one of them. The
/// host implements it from the directory the image carries; Core never reads the file system.
/// </summary>
public interface IMethodologyCatalog
{
    /// <summary>The names of the available Methodologies in ascending order.</summary>
    IReadOnlyList<string> ListNames();

    /// <summary>The Methodology of this name with all its files, or null when none is shipped under it.</summary>
    Task<Methodology?> ReadAsync(string name, CancellationToken cancellationToken);
}
