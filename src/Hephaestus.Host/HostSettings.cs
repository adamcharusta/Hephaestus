using Hephaestus.Core.Localization;
using Hephaestus.Persistence;

namespace Hephaestus.Host;

/// <summary>
/// What the host and the command line read from the environment. The language is the installation's; Story 1.9 moves it
/// into Installation Configuration, where the Owner sets it from the command line. The two directories are the mount points of
/// the <c>hephaestus-data</c> and <c>hephaestus-secrets</c> volumes (AR-39); only the host reads them.
/// </summary>
internal sealed record HostSettings(string ControlSocketPath, Language Language, string DataDirectory, string SecretsDirectory)
{
    public const string DefaultControlSocketPath = "/run/hephaestus/control.sock";
    public const string DefaultDataDirectory = "/var/lib/hephaestus/data";
    public const string DefaultSecretsDirectory = "/var/lib/hephaestus/secrets";
    public const string ControlSocketVariable = "HEPHAESTUS_CONTROL_SOCKET";
    public const string DataDirectoryVariable = "HEPHAESTUS_DATA_DIR";
    public const string SecretsDirectoryVariable = "HEPHAESTUS_SECRETS_DIR";
    public const string LanguageVariable = "HEPHAESTUS_LANGUAGE";

    public PersistenceOptions Persistence => new(DataDirectory, SecretsDirectory);

    public static HostSettings FromEnvironment(Func<string, string?> getVariable) => new(
        ValueOrDefault(getVariable(ControlSocketVariable), DefaultControlSocketPath),
        LanguageResolver.Installation(getVariable(LanguageVariable)),
        ValueOrDefault(getVariable(DataDirectoryVariable), DefaultDataDirectory),
        ValueOrDefault(getVariable(SecretsDirectoryVariable), DefaultSecretsDirectory));

    private static string ValueOrDefault(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
