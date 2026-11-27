namespace Hephaestus.Workspaces.Egress;

/// <summary>
/// The host names a step run may reach through its egress proxy (AD-7, NFR-4), applied by name: a host is allowed when it is on the list, written in lower or upper
/// case, and not otherwise; there is no wildcard and no suffix match. <see cref="Open"/> has no list, and lets any public host through. The proxy gets the list in the
/// <see cref="VariableName"/> variable of its container, as one line of names separated by commas.
/// </summary>
public sealed class EgressAllowlist
{
    /// <summary>The environment variable of the proxy's container that holds the list; absent means no list.</summary>
    public const string VariableName = "HEPHAESTUS_EGRESS_ALLOWED_HOSTS";

    private readonly HashSet<string>? _hosts;

    private EgressAllowlist(HashSet<string>? hosts) => _hosts = hosts;

    /// <summary>No list: any public host is allowed.</summary>
    public static EgressAllowlist Open { get; } = new(null);

    /// <summary>A list of host names; an empty list allows none.</summary>
    public static EgressAllowlist Of(IEnumerable<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        return new([.. hosts.Select(Normalize).Where(host => host.Length > 0)]);
    }

    /// <summary>The list as the proxy's environment holds it: null for none, so that nothing is set, and the names separated by commas otherwise.</summary>
    public static string? Serialize(IReadOnlyList<string>? hosts) => hosts is null ? null : string.Join(',', hosts.Select(Normalize).Where(host => host.Length > 0));

    /// <summary>The list a proxy was started with: its variable absent is no list, and present, even empty, is the list it holds.</summary>
    public static EgressAllowlist Parse(string? variable) => variable is null ? Open : Of(variable.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    public bool IsOpen => _hosts is null;

    public bool Allows(string host) => _hosts is null || _hosts.Contains(Normalize(host));

    /// <summary>A host as the list and the requests hold it: lower case, with no trailing dot, which names the same host.</summary>
    public static string Normalize(string host) => host.Trim().TrimEnd('.').ToLowerInvariant();
}
