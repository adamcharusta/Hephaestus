using System.Net;
using System.Net.Sockets;

namespace Hephaestus.Workspaces.Egress;

/// <summary>What the egress proxy does with the network, and its limits. Tests replace the two calls to the network; nothing else changes how the rules are applied.</summary>
public sealed class EgressProxyOptions
{
    /// <summary>Resolves a host name to its addresses. Every address it returns is tested, not the first.</summary>
    public Func<string, CancellationToken, Task<IPAddress[]>> Resolve { get; init; } = (host, token) => Dns.GetHostAddressesAsync(host, token);

    /// <summary>Connects to an address that has passed the test of <see cref="AddressPolicy"/>; the proxy never connects by name, so the address it tested is the one it reaches.</summary>
    public Func<IPAddress, int, CancellationToken, Task<Socket>> Connect { get; init; } = ConnectAsync;

    /// <summary>The connections served at once; one more is told to try again.</summary>
    public int MaximumConnections { get; init; } = 256;

    /// <summary>How long a client has to send the head of its request.</summary>
    public TimeSpan HeadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a connection to a destination may take.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a connection may carry nothing, in either direction, before it is closed.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    private static async Task<Socket> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
