namespace Hephaestus.Core.Workspaces;

/// <summary>
/// What the runtime sets in the environment of every step container so that its traffic goes to the egress proxy (AD-7, Story 3.4): the standard proxy
/// variables in both cases, with the container's own loopback left out, and git's rewrite of a <c>github.com</c> SSH address to HTTPS, which an HTTP proxy can carry.
/// Nothing in it is specific to a language or build tool; such settings belong to the Project's image. A variable of an invocation with one of these names is replaced.
/// </summary>
public static class EgressEnvironment
{
    /// <summary>The destinations that never go through the proxy: the container's own loopback.</summary>
    public const string NoProxy = "localhost,127.0.0.1,::1";

    public static IReadOnlyDictionary<string, string> Variables { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HTTP_PROXY"] = EgressProxyAddress.Url,
        ["HTTPS_PROXY"] = EgressProxyAddress.Url,
        ["NO_PROXY"] = NoProxy,
        ["http_proxy"] = EgressProxyAddress.Url,
        ["https_proxy"] = EgressProxyAddress.Url,
        ["no_proxy"] = NoProxy,
        // Git reads these in place of a configuration file. Both forms of an SSH address of github.com become the same HTTPS address.
        ["GIT_CONFIG_COUNT"] = "2",
        ["GIT_CONFIG_KEY_0"] = "url.https://github.com/.insteadOf",
        ["GIT_CONFIG_VALUE_0"] = "git@github.com:",
        ["GIT_CONFIG_KEY_1"] = "url.https://github.com/.insteadOf",
        ["GIT_CONFIG_VALUE_1"] = "ssh://git@github.com/",
    };
}
