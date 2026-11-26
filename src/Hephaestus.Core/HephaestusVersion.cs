using System.Reflection;

namespace Hephaestus.Core;

/// <summary>The version of this build, from the assembly's informational version without its build metadata.</summary>
public static class HephaestusVersion
{
    public static string Current { get; } = Read(typeof(HephaestusVersion).Assembly);

    internal static string Read(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}
