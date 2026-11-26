using Hephaestus.Core.Localization;

namespace Hephaestus.Host.Tests;

/// <summary>
/// A socket path in a directory of its own, removed with it, and beside it the data and secrets directories an installation
/// needs. Unix socket paths are short, so it lives under /tmp.
/// </summary>
internal sealed class TempSocket : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hx").FullName;

    public string Path => System.IO.Path.Combine(_directory, "control.sock");

    public string DataDirectory => System.IO.Path.Combine(_directory, "data");

    public string SecretsDirectory => System.IO.Path.Combine(_directory, "secrets");

    public HostSettings Settings(Language language = Language.English) => new(Path, language, DataDirectory, SecretsDirectory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
