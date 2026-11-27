using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Hephaestus.Workspaces.Egress;

/// <summary>
/// The egress proxy (AD-7, AR-41, Story 3.4): a forward proxy for HTTP and HTTPS that is the one way out of a step run's network. It runs in a container of its own,
/// as <c>hephaestus egress-proxy</c>, with no credential, volume, or Docker socket, and it is never part of the orchestrator's process. It resolves the name itself, refuses the request
/// when any address it resolves to is not public or when the step run's list does not name the host, and otherwise connects to the address it tested and carries the bytes. It
/// does not look inside a tunnel and does not terminate TLS. A refusal is one line of its log, which is how the runtime learns which hosts a step run was refused (AD-18).
/// </summary>
public sealed class EgressProxyServer : IAsyncDisposable
{
    private const int MaximumHeadBytes = 16 * 1024;
    private static readonly byte[] HeadEnd = "\r\n\r\n"u8.ToArray();

    private readonly EgressAllowlist _allowlist;
    private readonly EgressProxyOptions _options;
    private readonly Action<string> _log;
    private readonly Socket _listener = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentDictionary<Task, bool> _connections = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly HashSet<string> _refused = new(StringComparer.Ordinal);
    private int _disposed;

    /// <param name="log">Where a line of the log goes: the ready line and one line for each host refused.</param>
    public EgressProxyServer(EgressAllowlist allowlist, EgressProxyOptions options, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(allowlist);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _allowlist = allowlist;
        _options = options;
        _log = log;
        _slots = new SemaphoreSlim(options.MaximumConnections);
    }

    /// <summary>The port the proxy listens on once <see cref="Start"/> has run.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndPoint!).Port;

    /// <summary>Binds the listener and says so in the log; a request that arrives before this is refused by the network, so the runtime waits for the line.</summary>
    public void Start(IPEndPoint endpoint)
    {
        _listener.Bind(endpoint);
        _listener.Listen(128);
        _log(EgressRefusals.ReadyLine(Port));
    }

    /// <summary>Accepts connections until <paramref name="stopping"/> is cancelled or the server is disposed.</summary>
    public async Task RunAsync(CancellationToken stopping)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, _stopping.Token);
        try
        {
            while (true)
            {
                Socket client;
                try
                {
                    client = await _listener.AcceptAsync(linked.Token);
                }
                catch (SocketException exception) when (!linked.IsCancellationRequested)
                {
                    // One connection could not be accepted, for example because no descriptor was free; the proxy goes on, because a step without it has no way out.
                    _log($"error {exception.SocketErrorCode}");
                    await Task.Delay(100, linked.Token);
                    continue;
                }

                if (!_slots.Wait(0))
                {
                    using (client)
                    {
                        await RefuseAsync(client, "503 Service Unavailable", "The egress proxy is serving as many connections as it will; try again.", linked.Token);
                    }

                    continue;
                }

                var handling = Task.Run(() => HandleAsync(client, linked.Token), CancellationToken.None);
                _connections[handling] = true;
                _ = handling.ContinueWith(finished => _connections.TryRemove(finished, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException && linked.IsCancellationRequested)
        {
            // Stopped.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stopping.CancelAsync();
        _listener.Dispose();
        await Task.WhenAll(_connections.Keys);
        _stopping.Dispose();
        _slots.Dispose();
    }

    private async Task HandleAsync(Socket client, CancellationToken stopping)
    {
        using (client)
        {
            try
            {
                await ServeAsync(client, stopping);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
            {
                // The client went away, or the proxy is stopping; there is no one to tell.
            }
            catch (Exception exception)
            {
                // A fault of the proxy's own ends this connection and no other. Its type is logged, and nothing the client sent.
                _log($"error {exception.GetType().Name}");
            }
            finally
            {
                _slots.Release();
            }
        }
    }

    private async Task ServeAsync(Socket client, CancellationToken stopping)
    {
        var (head, leftover) = await ReadHeadAsync(client, stopping);
        if (head is null)
        {
            await RefuseAsync(client, "400 Bad Request", "The head of the request was not sent in time, was too large, or was not complete.", stopping);
            return;
        }

        var (request, problem) = ProxyRequest.Parse(head);
        if (request is null)
        {
            await RefuseAsync(client, "400 Bad Request", $"The egress proxy cannot carry this request: {problem}.", stopping);
            return;
        }

        if (!_allowlist.Allows(request.Host))
        {
            Refuse(request.Host, EgressRefusals.NotAllowed);
            await RefuseAsync(client, "403 Forbidden", $"The egress proxy refused {request.Host}: it is not on the list of hosts this step may reach.", stopping);
            return;
        }

        var addresses = await ResolveAsync(request.Host, stopping);
        if (addresses.Length == 0)
        {
            await RefuseAsync(client, "502 Bad Gateway", $"The egress proxy could not find {request.Host}.", stopping);
            return;
        }

        // One address that is not public refuses the request: a name that has a private address among its answers is the way a rebinding attack is made.
        if (addresses.Any(address => !AddressPolicy.IsPublic(address)))
        {
            Refuse(request.Host, EgressRefusals.PrivateAddress);
            await RefuseAsync(client, "403 Forbidden", $"The egress proxy refused {request.Host}: it is not a public address.", stopping);
            return;
        }

        // A tunnel carries anything, so it is given to the two ports of HTTP and HTTPS alone: SSH, mail, and the rest have no way out (NFR-4).
        if (request.IsTunnel && request.Port is not (443 or 80))
        {
            Refuse(request.Host, EgressRefusals.PortNotAllowed);
            await RefuseAsync(client, "403 Forbidden", $"The egress proxy refused {request.Host}:{request.Port}: a tunnel goes to port 443 or 80 only.", stopping);
            return;
        }

        var upstream = await ConnectAsync(addresses, request.Port, stopping);
        if (upstream is null)
        {
            await RefuseAsync(client, "502 Bad Gateway", $"The egress proxy could not connect to {request.Host}.", stopping);
            return;
        }

        using (upstream)
        {
            if (request.IsTunnel)
            {
                await client.SendAsync("HTTP/1.1 200 Connection established\r\n\r\n"u8.ToArray(), stopping);
            }
            else
            {
                await upstream.SendAsync(request.ToOriginForm(), stopping);
            }

            if (leftover.Length > 0)
            {
                await upstream.SendAsync(leftover, stopping);
            }

            await RelayAsync(client, upstream, stopping);
        }
    }

    /// <summary>Records a refusal once for each host, up to the most one step run records, so that a step cannot fill the log.</summary>
    private void Refuse(string host, string reason)
    {
        if (EgressRefusals.Loggable(host) is not { } loggable)
        {
            return;
        }

        lock (_refused)
        {
            if (_refused.Count < EgressRefusals.MaximumHosts && _refused.Add(loggable))
            {
                _log(EgressRefusals.RefusalLine(loggable, reason));
            }
        }
    }

    private async Task<IPAddress[]> ResolveAsync(string host, CancellationToken stopping)
    {
        var literal = host.Trim('[', ']');
        if (IPAddress.TryParse(literal, out var address))
        {
            return [address];
        }

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            limit.CancelAfter(_options.ConnectTimeout);
            return await _options.Resolve(host, limit.Token);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException or OperationCanceledException && !stopping.IsCancellationRequested)
        {
            return [];
        }
    }

    private async Task<Socket?> ConnectAsync(IPAddress[] addresses, int port, CancellationToken stopping)
    {
        foreach (var address in addresses)
        {
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                limit.CancelAfter(_options.ConnectTimeout);
                return await _options.Connect(address, port, limit.Token);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException && !stopping.IsCancellationRequested)
            {
                // The next address, if there is one.
            }
        }

        return null;
    }

    /// <summary>The head of the request, and whatever the client sent after it in the same read, which belongs to the body or to the tunnel; null for a head that did not arrive.</summary>
    private async Task<(byte[]? Head, byte[] Leftover)> ReadHeadAsync(Socket client, CancellationToken stopping)
    {
        var buffer = new byte[MaximumHeadBytes];
        var length = 0;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        limit.CancelAfter(_options.HeadTimeout);
        try
        {
            while (length < buffer.Length)
            {
                var read = await client.ReceiveAsync(buffer.AsMemory(length), limit.Token);
                if (read == 0)
                {
                    return (null, []);
                }

                // The end can straddle two reads, so the search starts a little before what is new.
                var from = Math.Max(0, length - HeadEnd.Length + 1);
                length += read;
                var end = buffer.AsSpan(from, length - from).IndexOf(HeadEnd);
                if (end >= 0)
                {
                    var headLength = from + end;
                    return (buffer[..headLength], buffer[(headLength + HeadEnd.Length)..length]);
                }
            }
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
            // Too slow.
        }

        return (null, []);
    }

    /// <summary>Carries the bytes in both directions until both have ended; when one side ends, the other is told, so that a half-closed tunnel still completes.</summary>
    private async Task RelayAsync(Socket client, Socket upstream, CancellationToken stopping)
    {
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var toUpstream = CopyAsync(client, upstream, ending);
        var toClient = CopyAsync(upstream, client, ending);
        await Task.WhenAll(toUpstream, toClient);
    }

    private async Task CopyAsync(Socket source, Socket target, CancellationTokenSource ending)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ending.Token);
                idle.CancelAfter(_options.IdleTimeout);
                var read = await source.ReceiveAsync(buffer, idle.Token);
                if (read == 0)
                {
                    target.Shutdown(SocketShutdown.Send);
                    return;
                }

                await target.SendAsync(buffer.AsMemory(0, read), ending.Token);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
        {
            // One side failed or was idle for too long: the connection ends in both directions.
            await ending.CancelAsync();
        }
    }

    private static async Task RefuseAsync(Socket client, string status, string reason, CancellationToken stopping)
    {
        try
        {
            var body = Encoding.UTF8.GetBytes(reason + "\n");
            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await client.SendAsync(head, stopping);
            await client.SendAsync(body, stopping);
            client.Shutdown(SocketShutdown.Send);
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
        {
            // The client has gone; the refusal is already in the log.
        }
    }
}
