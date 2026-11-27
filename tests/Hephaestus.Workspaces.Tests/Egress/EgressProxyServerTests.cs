using System.Net;
using System.Net.Sockets;
using System.Text;
using Hephaestus.Workspaces.Egress;

namespace Hephaestus.Workspaces.Tests.Egress;

/// <summary>
/// Story 3.4, AD-7, AR-47, NFR-4: the proxy on real loopback sockets, with name resolution and the connection to the destination replaced, so that what it refuses and what it carries is
/// shown without a network. "The origin" is a listener of the test on loopback that the proxy is made to connect to in place of the public address a name resolves to.
/// </summary>
public sealed class EgressProxyServerTests : IAsyncDisposable
{
    private static readonly IPAddress Public = IPAddress.Parse("93.184.216.34");
    private static readonly IPAddress Private = IPAddress.Parse("192.168.1.10");

    private readonly List<string> _log = [];
    private readonly Dictionary<string, IPAddress[]> _names = new(StringComparer.Ordinal) { ["example.com"] = [Public], ["api.nuget.org"] = [Public], ["intranet.local"] = [Private], ["rebinding.example"] = [Public, Private] };
    private readonly List<(IPAddress Address, int Port)> _connections = [];
    private readonly TcpListener _origin = new(IPAddress.Loopback, 0);
    private EgressProxyServer? _server;
    private Task? _running;
    private CancellationTokenSource? _stopping;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<int> StartAsync(EgressAllowlist? allowlist = null, Action<EgressProxyOptions>? configure = null)
    {
        _origin.Start();
        var options = new EgressProxyOptions
        {
            Resolve = (host, _) => _names.TryGetValue(host, out var addresses) ? Task.FromResult(addresses) : Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)),
            Connect = async (address, port, token) =>
            {
                _connections.Add((address, port));
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync((IPEndPoint)_origin.LocalEndpoint, token);
                return socket;
            },
        };
        _server = new EgressProxyServer(allowlist ?? EgressAllowlist.Open, options, line => { lock (_log) { _log.Add(line); } });
        _server.Start(new IPEndPoint(IPAddress.Loopback, 0));
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        _running = _server.RunAsync(_stopping.Token);
        return _server.Port;
    }

    /// <summary>An origin that answers one request with a fixed response and reports what it received.</summary>
    private async Task<string> ServeOriginOnceAsync(string response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok")
    {
        using var client = await _origin.AcceptTcpClientAsync(Cancel);
        using var stream = client.GetStream();
        var buffer = new byte[64 * 1024];
        var received = new StringBuilder();
        while (true)
        {
            var read = await stream.ReadAsync(buffer, Cancel);
            if (read == 0)
            {
                break;
            }

            received.Append(Encoding.Latin1.GetString(buffer, 0, read));
            if (received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        await stream.WriteAsync(Encoding.Latin1.GetBytes(response), Cancel);
        client.Client.Shutdown(SocketShutdown.Send);
        return received.ToString();
    }

    private static async Task<string> SendAsync(int port, string request, bool readToEnd = true)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, Cancel);
        using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.Latin1.GetBytes(request), Cancel);
        return await ReadAsync(stream, readToEnd);
    }

    private static async Task<string> ReadAsync(NetworkStream stream, bool toEnd)
    {
        var buffer = new byte[64 * 1024];
        var received = new StringBuilder();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            var read = await stream.ReadAsync(buffer, limit.Token);
            if (read == 0)
            {
                break;
            }

            received.Append(Encoding.Latin1.GetString(buffer, 0, read));
            if (!toEnd && received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        return received.ToString();
    }

    [Fact]
    public async Task The_proxy_says_it_is_listening_in_its_log_once_it_does()
    {
        var port = await StartAsync();

        Assert.Equal([EgressRefusals.ReadyLine(port)], _log);
        Assert.True(EgressRefusals.IsReady(string.Join('\n', _log)));
    }

    [Fact]
    public async Task A_tunnel_to_an_allowed_public_host_is_established_and_carries_bytes_both_ways()
    {
        var port = await StartAsync();
        var origin = ServeOriginOnceAsync("pong");

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, Cancel);
        using var stream = client.GetStream();
        await stream.WriteAsync("CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n"u8.ToArray(), Cancel);
        var established = await ReadAsync(stream, toEnd: false);
        Assert.StartsWith("HTTP/1.1 200 Connection established\r\n", established, StringComparison.Ordinal);
        await stream.WriteAsync("ping\r\n\r\n"u8.ToArray(), Cancel);
        var answer = await ReadAsync(stream, toEnd: true);

        Assert.Equal("pong", answer);
        Assert.Contains("ping", await origin, StringComparison.Ordinal);
        Assert.Equal([(Public, 443)], _connections);
        Assert.DoesNotContain(_log, line => line.StartsWith("refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bytes_the_client_sent_with_the_head_of_the_tunnel_reach_the_origin()
    {
        var port = await StartAsync();
        var origin = ServeOriginOnceAsync("pong");

        var answer = await SendAsync(port, "CONNECT example.com:443 HTTP/1.1\r\n\r\nearly\r\n\r\n");

        Assert.EndsWith("pong", answer, StringComparison.Ordinal);
        Assert.Contains("early", await origin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plain_http_request_to_an_allowed_host_is_passed_in_origin_form_and_its_response_returned()
    {
        var port = await StartAsync(EgressAllowlist.Of(["example.com"]));
        var origin = ServeOriginOnceAsync();

        var answer = await SendAsync(port, "GET http://example.com/crl.pem HTTP/1.1\r\nHost: example.com\r\nUser-Agent: dotnet\r\n\r\n");

        Assert.Equal("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", answer);
        Assert.Equal("GET /crl.pem HTTP/1.1\r\nHost: example.com\r\nUser-Agent: dotnet\r\nConnection: close\r\n\r\n", await origin);
        Assert.Equal([(Public, 80)], _connections);
    }

    [Fact]
    public async Task A_host_outside_the_list_is_refused_with_403_and_recorded_once_in_the_log()
    {
        var port = await StartAsync(EgressAllowlist.Of(["api.nuget.org"]));

        var first = await SendAsync(port, "CONNECT example.com:443 HTTP/1.1\r\n\r\n");
        var second = await SendAsync(port, "GET http://example.com/ HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403 Forbidden\r\n", first, StringComparison.Ordinal);
        Assert.Contains("not on the list", first, StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 403 Forbidden\r\n", second, StringComparison.Ordinal);
        Assert.Equal([EgressRefusals.ReadyLine(port), "refused example.com not-allowed"], _log);
        Assert.Empty(_connections);
    }

    [Fact]
    public async Task A_host_on_the_list_is_reached_and_a_vendor_host_the_project_did_not_list_is_not()
    {
        var port = await StartAsync(EgressAllowlist.Of(["api.nuget.org"]));
        var origin = ServeOriginOnceAsync();

        var allowed = await SendAsync(port, "GET http://api.nuget.org/v3/index.json HTTP/1.1\r\n\r\n");
        var vendor = await SendAsync(port, "CONNECT api.anthropic.com:443 HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 200 OK", allowed, StringComparison.Ordinal);
        Assert.Contains("GET /v3/index.json", await origin, StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 403 Forbidden", vendor, StringComparison.Ordinal);
        Assert.Equal(["api.anthropic.com"], EgressRefusals.Parse(string.Join('\n', _log)));
    }

    [Theory]
    [InlineData("intranet.local:443")]
    [InlineData("127.0.0.1:8717")]
    [InlineData("10.0.0.5:80")]
    [InlineData("169.254.169.254:80")]
    [InlineData("[::1]:8717")]
    [InlineData("[fe80::1]:443")]
    [InlineData("rebinding.example:443")]
    public async Task A_destination_that_is_or_resolves_to_a_loopback_link_local_or_private_address_is_refused_whatever_the_list_says(string destination)
    {
        var port = await StartAsync();

        var answer = await SendAsync(port, $"CONNECT {destination} HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403 Forbidden\r\n", answer, StringComparison.Ordinal);
        Assert.Contains("not a public address", answer, StringComparison.Ordinal);
        Assert.Single(_log, line => line.EndsWith(" private-address", StringComparison.Ordinal));
        Assert.Empty(_connections);
    }

    [Fact]
    public async Task A_private_destination_of_a_plain_request_is_refused_too()
    {
        var port = await StartAsync();

        var answer = await SendAsync(port, "GET http://192.168.1.1/ HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403 Forbidden\r\n", answer, StringComparison.Ordinal);
        Assert.Contains("refused 192.168.1.1 private-address", _log);
    }

    [Fact]
    public async Task The_proxy_connects_to_the_address_it_tested_and_never_to_the_name_again()
    {
        var port = await StartAsync();
        var origin = ServeOriginOnceAsync();

        await SendAsync(port, "GET http://example.com/ HTTP/1.1\r\n\r\n");

        await origin;
        Assert.Equal([(Public, 80)], _connections);
    }

    [Fact]
    public async Task A_name_that_cannot_be_resolved_is_a_502_and_not_a_refusal()
    {
        var port = await StartAsync();

        var answer = await SendAsync(port, "CONNECT nowhere.invalid:443 HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 502 Bad Gateway\r\n", answer, StringComparison.Ordinal);
        Assert.DoesNotContain(_log, line => line.StartsWith("refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_destination_that_does_not_answer_is_a_502()
    {
        var port = await StartAsync(configure: null);
        _origin.Stop();

        var answer = await SendAsync(port, "CONNECT example.com:443 HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 502 Bad Gateway\r\n", answer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET /relative HTTP/1.1\r\n\r\n")]
    [InlineData("GET https://example.com/ HTTP/1.1\r\n\r\n")]
    [InlineData("GET http://example.com/ HTTP/1.1\r\nContent-Length: 1\r\nTransfer-Encoding: chunked\r\n\r\n")]
    [InlineData("GARBAGE\r\n\r\n")]
    public async Task What_the_proxy_cannot_carry_is_a_400_and_nothing_is_connected(string request)
    {
        var port = await StartAsync();

        var answer = await SendAsync(port, request);

        Assert.StartsWith("HTTP/1.1 400 Bad Request\r\n", answer, StringComparison.Ordinal);
        Assert.Empty(_connections);
    }

    [Fact]
    public async Task A_head_that_does_not_arrive_in_time_is_a_400()
    {
        var port = await StartAsync(configure: null);
        await _server!.DisposeAsync();
        _server = new EgressProxyServer(EgressAllowlist.Open, new EgressProxyOptions { HeadTimeout = TimeSpan.FromMilliseconds(200) }, line => { lock (_log) { _log.Add(line); } });
        _server.Start(new IPEndPoint(IPAddress.Loopback, 0));
        _running = _server.RunAsync(_stopping!.Token);

        var answer = await SendAsync(_server.Port, "CONNECT example.com:443 HTTP/1.1\r\n");

        Assert.StartsWith("HTTP/1.1 400 Bad Request\r\n", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_head_larger_than_the_proxy_reads_is_a_400()
    {
        var port = await StartAsync();

        var answer = await SendAsync(port, "GET http://example.com/ HTTP/1.1\r\nX: " + new string('a', 20 * 1024) + "\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 400 Bad Request\r\n", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_client_that_goes_away_before_sending_anything_is_not_an_error()
    {
        var port = await StartAsync();

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, port, Cancel);
        }

        var answer = await SendAsync(port, "GARBAGE\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 400", answer, StringComparison.Ordinal);
        Assert.DoesNotContain(_log, line => line.StartsWith("error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task One_connection_more_than_the_limit_is_told_to_try_again()
    {
        await StartAsync();
        await _server!.DisposeAsync();
        _server = new EgressProxyServer(EgressAllowlist.Open, new EgressProxyOptions { MaximumConnections = 1, HeadTimeout = TimeSpan.FromSeconds(30) }, line => { lock (_log) { _log.Add(line); } });
        _server.Start(new IPEndPoint(IPAddress.Loopback, 0));
        _running = _server.RunAsync(_stopping!.Token);

        using var held = new TcpClient();
        await held.ConnectAsync(IPAddress.Loopback, _server.Port, Cancel);
        await held.GetStream().WriteAsync("CONNECT"u8.ToArray(), Cancel);
        await Task.Delay(100, Cancel);
        var answer = await SendAsync(_server.Port, "CONNECT example.com:443 HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 503 Service Unavailable\r\n", answer, StringComparison.Ordinal);
        // The refused connection was closed by the proxy: a read of it ends, and the one slot is still held by the first client.
        held.Close();
        await Task.Delay(100, Cancel);
        var afterRelease = await SendAsync(_server.Port, "CONNECT 127.0.0.1:1 HTTP/1.1\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 403 Forbidden\r\n", afterRelease, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(25)]
    [InlineData(8443)]
    public async Task A_tunnel_to_a_port_that_is_neither_https_nor_http_is_refused_and_recorded(int port)
    {
        var proxyPort = await StartAsync();

        var answer = await SendAsync(proxyPort, $"CONNECT example.com:{port} HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403 Forbidden\r\n", answer, StringComparison.Ordinal);
        Assert.Contains("port 443 or 80 only", answer, StringComparison.Ordinal);
        Assert.Contains("refused example.com port-not-allowed", _log);
        Assert.Empty(_connections);
    }

    [Fact]
    public async Task A_tunnel_to_port_80_is_allowed_like_one_to_443()
    {
        var port = await StartAsync();
        var origin = ServeOriginOnceAsync("pong");

        var answer = await SendAsync(port, "CONNECT example.com:80 HTTP/1.1\r\n\r\nping\r\n\r\n");

        Assert.EndsWith("pong", answer, StringComparison.Ordinal);
        await origin;
        Assert.Equal([(Public, 80)], _connections);
    }

    [Fact]
    public async Task A_refused_host_is_logged_once_however_often_it_is_asked_for_and_at_most_a_hundred_are()
    {
        var port = await StartAsync(EgressAllowlist.Of([]));
        foreach (var i in Enumerable.Range(0, 120))
        {
            await SendAsync(port, $"CONNECT host{i}.example:443 HTTP/1.1\r\n\r\n");
        }

        await SendAsync(port, "CONNECT host0.example:443 HTTP/1.1\r\n\r\n");

        Assert.Equal(100, _log.Count(line => line.StartsWith("refused", StringComparison.Ordinal)));
        Assert.Single(_log, line => line == "refused host0.example not-allowed");
    }

    [Fact]
    public async Task Disposing_the_server_stops_it_and_ends_its_run()
    {
        var port = await StartAsync();

        await _server!.DisposeAsync();
        await _running!.WaitAsync(TimeSpan.FromSeconds(5), Cancel);

        using var client = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback, port, Cancel).AsTask());
        await _server.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        if (_running is not null)
        {
            await _running.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }

        _stopping?.Dispose();
        _origin.Dispose();
    }
}
