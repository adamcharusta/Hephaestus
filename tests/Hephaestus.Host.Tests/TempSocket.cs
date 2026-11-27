using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;
using Hephaestus.Workspaces.Tests;

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

    public string AgentsDirectory => System.IO.Path.Combine(_directory, "agents");

    public string AgentsLockFile => System.IO.Path.Combine(_directory, "agents.lock");

    public string CaBundleFile => System.IO.Path.Combine(_directory, "image-ca.crt");

    public HostSettings Settings(Language language = Language.English) => new(Path, language, DataDirectory, SecretsDirectory, AgentsDirectory, AgentsLockFile, CaBundleFile, HostSettings.DefaultInstallationId);

    /// <summary>
    /// A lock, a certificate bundle, and a volume that already holds the executables the lock pins for this machine, so that a start downloads
    /// nothing. Returns the vendors, for a test that wants to change what the lock says.
    /// </summary>
    public FakeVendor PrepareAgents()
    {
        var vendor = new FakeVendor();
        File.WriteAllText(AgentsLockFile, vendor.LockText);
        File.WriteAllText(CaBundleFile, "certificates of the image");
        vendor.Install(AgentsDirectory, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? AgentArchitecture.Arm64 : AgentArchitecture.X64);
        return vendor;
    }

    /// <summary>The variables a start of the host reads, for this installation.</summary>
    public Dictionary<string, string?> Environment() => new()
    {
        [HostSettings.ControlSocketVariable] = Path,
        [HostSettings.DataDirectoryVariable] = DataDirectory,
        [HostSettings.SecretsDirectoryVariable] = SecretsDirectory,
        [HostSettings.AgentsDirectoryVariable] = AgentsDirectory,
        [HostSettings.AgentsLockVariable] = AgentsLockFile,
        [HostSettings.CaBundleVariable] = CaBundleFile,
    };

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
