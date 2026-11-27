using Hephaestus.Core.Localization;
using Hephaestus.Persistence;

namespace Hephaestus.Host;

/// <summary>
/// What the host and the command line read from the environment. The installation's language is a setting the Owner stores
/// (Story 1.9); <see cref="Language"/> is what the environment names, English when it names none, and serves only an installation that has
/// stored none and a command that cannot ask the host. The three directories are the mount points of the <c>hephaestus-data</c>,
/// <c>hephaestus-secrets</c>, and <c>hephaestus-agents</c> volumes (AR-39); only the host reads or writes them. The lock is the <c>deploy/agents.lock</c>
/// the image carries beside the executable, and the CA bundle is the image's own certificate store (Story 3.1). The installation id is the value of
/// the <c>hephaestus.installation</c> label on every container, volume, and network the host creates (Story 3.2); a development installation on the same
/// Docker host names its own, so that neither touches the other's objects. <see cref="EgressProxyImage"/> is the image the egress proxy of every step run is created from:
/// the <c>hephaestus</c> image itself, which Compose names (Story 3.4). <see cref="ToolsImage"/> is the image of the checkout container of every step, <c>hephaestus-tools</c>, and
/// <see cref="MirrorsDirectory"/> is the mount point of the <c>hephaestus-mirrors</c> volume, where the gateway keeps one bare mirror per Project; the bundles it writes are in <c>bundles</c> under the data directory (Story 3.5).
/// </summary>
internal sealed record HostSettings(string ControlSocketPath, Language Language, string DataDirectory, string SecretsDirectory, string AgentsDirectory, string AgentsLockFile, string CaBundleFile, string InstallationId)
{
    public const string DefaultControlSocketPath = "/run/hephaestus/control.sock";
    public const string DefaultDataDirectory = "/var/lib/hephaestus/data";
    public const string DefaultSecretsDirectory = "/var/lib/hephaestus/secrets";
    public const string DefaultAgentsDirectory = "/var/lib/hephaestus/agents";
    public const string DefaultCaBundleFile = "/etc/ssl/certs/ca-certificates.crt";
    public const string DefaultInstallationId = "hephaestus";
    public const string DefaultMirrorsDirectory = "/var/lib/hephaestus/mirrors";
    public const string DefaultEgressProxyImage = "ghcr.io/adamcharusta/hephaestus:latest";
    public const string DefaultToolsImage = "ghcr.io/adamcharusta/hephaestus-tools:latest";
    public const string BundlesDirectoryName = "bundles";
    public const string AgentsLockFileName = "agents.lock";
    public const string ControlSocketVariable = "HEPHAESTUS_CONTROL_SOCKET";
    public const string DataDirectoryVariable = "HEPHAESTUS_DATA_DIR";
    public const string SecretsDirectoryVariable = "HEPHAESTUS_SECRETS_DIR";
    public const string AgentsDirectoryVariable = "HEPHAESTUS_AGENTS_DIR";
    public const string AgentsLockVariable = "HEPHAESTUS_AGENTS_LOCK";
    public const string CaBundleVariable = "HEPHAESTUS_CA_BUNDLE";
    public const string LanguageVariable = "HEPHAESTUS_LANGUAGE";
    public const string InstallationIdVariable = "HEPHAESTUS_INSTALLATION_ID";
    public const string EgressProxyImageVariable = "HEPHAESTUS_EGRESS_PROXY_IMAGE";
    public const string MirrorsDirectoryVariable = "HEPHAESTUS_MIRRORS_DIR";
    public const string ToolsImageVariable = "HEPHAESTUS_TOOLS_IMAGE";

    /// <summary>The image of the egress proxy of a step run: the one this host runs from, so that the proxy always matches it.</summary>
    public string EgressProxyImage { get; init; } = DefaultEgressProxyImage;

    /// <summary>The image of the checkout container of every step: the tools image of the release this host is, which holds git and nothing else.</summary>
    public string ToolsImage { get; init; } = DefaultToolsImage;

    /// <summary>Where the bare mirrors of the Projects are kept: the <c>hephaestus-mirrors</c> volume, which only this container mounts.</summary>
    public string MirrorsDirectory { get; init; } = DefaultMirrorsDirectory;

    /// <summary>Where the gateway writes bundles and the runtime reads them: a directory of the data volume.</summary>
    public string BundlesDirectory => Path.Combine(DataDirectory, BundlesDirectoryName);

    public PersistenceOptions Persistence => new(DataDirectory, SecretsDirectory);

    public static HostSettings FromEnvironment(Func<string, string?> getVariable) => new(
        ValueOrDefault(getVariable(ControlSocketVariable), DefaultControlSocketPath),
        LanguageResolver.Installation(getVariable(LanguageVariable)),
        ValueOrDefault(getVariable(DataDirectoryVariable), DefaultDataDirectory),
        ValueOrDefault(getVariable(SecretsDirectoryVariable), DefaultSecretsDirectory),
        ValueOrDefault(getVariable(AgentsDirectoryVariable), DefaultAgentsDirectory),
        ValueOrDefault(getVariable(AgentsLockVariable), Path.Combine(AppContext.BaseDirectory, AgentsLockFileName)),
        ValueOrDefault(getVariable(CaBundleVariable), DefaultCaBundleFile),
        ValueOrDefault(getVariable(InstallationIdVariable), DefaultInstallationId))
    {
        EgressProxyImage = ValueOrDefault(getVariable(EgressProxyImageVariable), DefaultEgressProxyImage),
        ToolsImage = ValueOrDefault(getVariable(ToolsImageVariable), DefaultToolsImage),
        MirrorsDirectory = ValueOrDefault(getVariable(MirrorsDirectoryVariable), DefaultMirrorsDirectory),
    };

    private static string ValueOrDefault(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
