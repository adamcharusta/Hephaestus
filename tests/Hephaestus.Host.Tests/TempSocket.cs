namespace Hephaestus.Host.Tests;

/// <summary>A socket path in a directory of its own, removed with it. Unix socket paths are short, so it lives under /tmp.</summary>
internal sealed class TempSocket : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hx").FullName;

    public string Path => System.IO.Path.Combine(_directory, "control.sock");

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
