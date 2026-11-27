using System.Text.RegularExpressions;

namespace Hephaestus.Workspaces.Egress;

/// <summary>
/// What the egress proxy writes to its log, and what the runtime reads back from it (AD-18): the line that says it is ready, and one line for each host it refused. The
/// runtime carries the hosts on the step-exited Signal, so the proxy and the runtime need no channel besides the container's log.
/// </summary>
public static partial class EgressRefusals
{
    /// <summary>Why a request was refused: the host is not on the step run's list.</summary>
    public const string NotAllowed = "not-allowed";

    /// <summary>Why a request was refused: the name resolves to an address that is not public.</summary>
    public const string PrivateAddress = "private-address";

    /// <summary>Why a request was refused: a tunnel to a port that is neither HTTPS nor HTTP.</summary>
    public const string PortNotAllowed = "port-not-allowed";

    /// <summary>The most hosts one step run records, so that a step cannot make its Signal as large as it likes.</summary>
    public const int MaximumHosts = 100;

    /// <summary>The longest host name there is.</summary>
    public const int MaximumHostLength = 253;

    public static string ReadyLine(int port) => $"listening {port}";

    public static bool IsReady(string log) => ReadyPattern().IsMatch(log);

    public static string RefusalLine(string host, string reason) => $"refused {host} {reason}";

    /// <summary>
    /// The hosts the log says were refused, each once, in the order they were first refused, at most <see cref="MaximumHosts"/>. A line that is not a refusal, or whose host
    /// is not made of the characters a host name or an address is, is not one: the log holds only what the proxy wrote, but a name a step asked for is what it names.
    /// </summary>
    public static IReadOnlyList<string> Parse(string log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var hosts = new List<string>();
        foreach (Match line in RefusalPattern().Matches(log))
        {
            var host = line.Groups["host"].Value;
            if (!hosts.Contains(host, StringComparer.Ordinal))
            {
                hosts.Add(host);
                if (hosts.Count == MaximumHosts)
                {
                    break;
                }
            }
        }

        return hosts;
    }

    /// <summary>A host the way it is logged: the characters of a name or an address, and no more than a name can have; otherwise null, because it cannot be put in a line.</summary>
    public static string? Loggable(string host) => host.Length is > 0 and <= MaximumHostLength && HostPattern().IsMatch(host) ? host : null;

    [GeneratedRegex(@"^listening \d+\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ReadyPattern();

    [GeneratedRegex(@"^refused (?<host>[A-Za-z0-9._:\[\]-]{1,253}) (?:not-allowed|private-address|port-not-allowed)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex RefusalPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._:\[\]-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();
}
