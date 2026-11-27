using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;

namespace Hephaestus.Core.Workspaces;

/// <summary>
/// The processor and memory a step container may use (AD-7): what the Task's settings ask for, never more than the installation allows
/// (AD-16, NFR-5). A Task that asks for nothing, in an installation that allows nothing in particular, gets the defaults.
/// </summary>
/// <param name="CpuMillis">Thousandths of a processor.</param>
public sealed record StepLimits(int CpuMillis, long MemoryBytes)
{
    public const int DefaultCpuMillis = 2000;
    public const long DefaultMemoryBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>What the Project asks for, else the installation's ceiling, else the default; and the ceiling caps the result, so no repository can raise it.</summary>
    public static StepLimits Resolve(ProjectLimits requested, InstallationLimits installation)
    {
        var cpu = requested.CpuMillis ?? installation.CpuMillis ?? DefaultCpuMillis;
        var memory = requested.MemoryBytes ?? installation.MemoryBytes ?? DefaultMemoryBytes;
        return new StepLimits(
            installation.CpuMillis is { } cpuCeiling ? Math.Min(cpu, cpuCeiling) : cpu,
            installation.MemoryBytes is { } memoryCeiling ? Math.Min(memory, memoryCeiling) : memory);
    }
}
