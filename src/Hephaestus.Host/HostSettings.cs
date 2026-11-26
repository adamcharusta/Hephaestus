using Hephaestus.Core.Localization;

namespace Hephaestus.Host;

/// <summary>
/// What the host and the command line read from the environment. The language is the installation's; Story 1.9 moves it
/// into Installation Configuration, where the Owner sets it from the command line.
/// </summary>
internal sealed record HostSettings(string ControlSocketPath, Language Language)
{
    public const string DefaultControlSocketPath = "/run/hephaestus/control.sock";
    public const string ControlSocketVariable = "HEPHAESTUS_CONTROL_SOCKET";
    public const string LanguageVariable = "HEPHAESTUS_LANGUAGE";

    public static HostSettings FromEnvironment(Func<string, string?> getVariable)
    {
        var socket = getVariable(ControlSocketVariable);
        return new HostSettings(
            string.IsNullOrWhiteSpace(socket) ? DefaultControlSocketPath : socket,
            LanguageResolver.Installation(getVariable(LanguageVariable)));
    }
}
