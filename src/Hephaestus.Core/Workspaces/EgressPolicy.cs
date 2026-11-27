namespace Hephaestus.Core.Workspaces;

/// <summary>What a step run may reach when its Project lists no host (NFR-4).</summary>
public enum EgressDefault
{
    /// <summary>Any public host, unless the Project lists hosts. The value of v0.1.</summary>
    OpenUnlessListed,

    /// <summary>Only the hosts the Project lists and the Agent's own, so that a Project with no list reaches nothing else.</summary>
    AllowlistOnly,
}

/// <summary>
/// Which hosts a step run may reach (AD-7, NFR-4). <see cref="Default"/> is the installation's egress default and the only place it is set: making the
/// allowlist the default before Hephaestus is announced for general use is a change of that one value and of nothing else.
/// </summary>
public static class EgressPolicy
{
    /// <summary>The installation's egress default in v0.1: open unless a Project sets a list.</summary>
    public const EgressDefault Default = EgressDefault.OpenUnlessListed;

    /// <summary>
    /// The hosts one step run may reach, or null when any public host may be reached. They are the Project's list plus, only for a container that runs an
    /// Agent invocation, the hosts that invocation declares; a Validation container, which passes none, gets the Project's list alone.
    /// </summary>
    /// <param name="projectHosts">The Project's <c>allowedEgressHosts</c>; empty when it sets none.</param>
    /// <param name="invocationHosts">The hosts the Agent invocation of this container declares; null for a container that runs none.</param>
    /// <param name="installationDefault">What applies when <paramref name="projectHosts"/> is empty.</param>
    public static IReadOnlyList<string>? AllowedHosts(IReadOnlyList<string> projectHosts, IReadOnlyList<string>? invocationHosts, EgressDefault installationDefault = Default)
    {
        ArgumentNullException.ThrowIfNull(projectHosts);
        if (projectHosts.Count == 0 && installationDefault == EgressDefault.OpenUnlessListed)
        {
            return null;
        }

        return [.. projectHosts.Concat(invocationHosts ?? []).Select(host => host.Trim().ToLowerInvariant()).Where(host => host.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }
}
