namespace Hephaestus.Core.Workspaces;

/// <summary>
/// The one host name and port at which a step container reaches the egress proxy (AD-7, Story 3.4). They are part of the versioned public surface: a Project
/// whose build tool needs proxy settings of its own, such as Maven or Gradle, writes this address into its image, so a change of either is a changelog entry.
/// Every step run has a proxy of its own on a network of its own, and each is known by this name on that network alone.
/// </summary>
public static class EgressProxyAddress
{
    /// <summary>The name of the proxy on the step run's network.</summary>
    public const string Host = "hephaestus-egress";

    /// <summary>The port the proxy listens on, on that network.</summary>
    public const int Port = 3128;

    /// <summary>The address as the proxy variables of a step container hold it.</summary>
    public const string Url = "http://hephaestus-egress:3128";
}
