using System.Runtime.InteropServices;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Workspaces.Tests;

namespace Hephaestus.EndToEnd.Tests;

/// <summary>
/// What a start of the real executable needs of the agent binaries volume (Story 3.1): a lock, a certificate bundle, and a volume that already
/// holds the executables the lock pins for this machine, so the start downloads nothing and needs no network.
/// </summary>
internal static class AgentsEnvironment
{
    public static Dictionary<string, string> Prepare(string directory)
    {
        var vendor = new FakeVendor();
        var lockFile = Path.Combine(directory, "agents.lock");
        var caBundle = Path.Combine(directory, "image-ca.crt");
        var volume = Path.Combine(directory, "agents");
        // Once per directory: every process of a test, the command line included, asks for the same environment, and the host has since
        // made the executables read-only.
        if (!File.Exists(lockFile))
        {
            File.WriteAllText(lockFile, vendor.LockText);
            File.WriteAllText(caBundle, "certificates of the image");
            vendor.Install(volume, RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? AgentArchitecture.Arm64 : AgentArchitecture.X64);
        }

        return new Dictionary<string, string>
        {
            ["HEPHAESTUS_AGENTS_DIR"] = volume,
            ["HEPHAESTUS_AGENTS_LOCK"] = lockFile,
            ["HEPHAESTUS_CA_BUNDLE"] = caBundle,
        };
    }
}
