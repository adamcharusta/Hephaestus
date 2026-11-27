using System.Text;
using Hephaestus.Workspaces.Egress;

namespace Hephaestus.Workspaces.Tests.Egress;

/// <summary>Story 3.4: what the proxy makes of the head of a request, and what it refuses to carry.</summary>
public class ProxyRequestTests
{
    private static (ProxyRequest? Request, string? Problem) Parse(string head) => ProxyRequest.Parse(Encoding.Latin1.GetBytes(head));

    [Fact]
    public void A_tunnel_names_its_host_and_port()
    {
        var (request, problem) = Parse("CONNECT API.Anthropic.com:443 HTTP/1.1\r\nHost: api.anthropic.com:443\r\nProxy-Connection: keep-alive\r\n");

        Assert.Null(problem);
        Assert.True(request!.IsTunnel);
        Assert.Equal(("api.anthropic.com", 443), (request.Host, request.Port));
    }

    [Fact]
    public void A_tunnel_to_an_IPv6_address_in_brackets_is_read()
    {
        var (request, _) = Parse("CONNECT [2606:4700::1111]:443 HTTP/1.1\r\n");

        Assert.Equal(("2606:4700::1111", 443), (request!.Host, request.Port));
    }

    [Theory]
    [InlineData("CONNECT api.anthropic.com HTTP/1.1\r\n")]
    [InlineData("CONNECT api.anthropic.com:0 HTTP/1.1\r\n")]
    [InlineData("CONNECT api.anthropic.com:65536 HTTP/1.1\r\n")]
    [InlineData("CONNECT api.anthropic.com:abc HTTP/1.1\r\n")]
    [InlineData("CONNECT [2606:4700::1111] HTTP/1.1\r\n")]
    [InlineData("CONNECT [2606:4700::1111 HTTP/1.1\r\n")]
    [InlineData("CONNECT [2606:4700::1111]443 HTTP/1.1\r\n")]
    [InlineData("CONNECT :443 HTTP/1.1\r\n")]
    public void A_tunnel_without_a_host_and_a_valid_port_is_refused(string head)
    {
        var (request, problem) = Parse(head);

        Assert.Null(request);
        Assert.NotNull(problem);
    }

    [Fact]
    public void A_plain_request_names_its_host_the_default_port_and_the_path()
    {
        var (request, problem) = Parse("GET http://Example.com/path?q=1 HTTP/1.1\r\nHost: example.com\r\nUser-Agent: curl\r\n");

        Assert.Null(problem);
        Assert.False(request!.IsTunnel);
        Assert.Equal(("example.com", 80, "GET", "/path?q=1"), (request.Host, request.Port, request.Method, request.Path));
    }

    [Theory]
    [InlineData("GET http://example.com HTTP/1.1\r\n", "/")]
    [InlineData("GET http://example.com?q=1 HTTP/1.1\r\n", "/?q=1")]
    [InlineData("GET http://example.com:8080/x HTTP/1.1\r\n", "/x")]
    [InlineData("GET http://example.com#frag HTTP/1.1\r\n", "/")]
    [InlineData("GET http://example.com/x#frag HTTP/1.1\r\n", "/x")]
    public void A_plain_request_without_a_path_gets_the_root(string head, string path)
    {
        var (request, _) = Parse(head);

        Assert.Equal(path, request!.Path);
    }

    [Fact]
    public void A_plain_request_to_another_port_keeps_it()
    {
        var (request, _) = Parse("GET http://example.com:8080/x HTTP/1.1\r\n");

        Assert.Equal(8080, request!.Port);
    }

    [Fact]
    public void The_request_the_origin_gets_is_in_origin_form_with_the_checked_host_and_the_connection_closed_after_it()
    {
        var (request, _) = Parse("POST http://example.com:8080/api HTTP/1.1\r\nHost: evil.example\r\nContent-Length: 2\r\nProxy-Connection: keep-alive\r\nProxy-Authorization: x\r\nConnection: keep-alive\r\nAccept: */*\r\n");

        var origin = Encoding.Latin1.GetString(request!.ToOriginForm());

        Assert.Equal("POST /api HTTP/1.1\r\nHost: example.com:8080\r\nContent-Length: 2\r\nAccept: */*\r\nConnection: close\r\n\r\n", origin);
    }

    [Fact]
    public void The_host_header_of_an_IPv6_origin_is_in_brackets_and_the_default_port_is_left_out()
    {
        var (request, _) = Parse("GET http://[2606:4700::1111]/ HTTP/1.1\r\n");

        Assert.StartsWith("GET / HTTP/1.1\r\nHost: [2606:4700::1111]\r\n", Encoding.Latin1.GetString(request!.ToOriginForm()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET /path HTTP/1.1\r\n", "http address")]
    [InlineData("GET https://example.com/ HTTP/1.1\r\n", "http address")]
    [InlineData("GET ftp://example.com/ HTTP/1.1\r\n", "http address")]
    [InlineData("GET http://user@example.com/ HTTP/1.1\r\n", "not a host")]
    [InlineData("GET http:/// HTTP/1.1\r\n", "not a host")]
    [InlineData("GET http://example.com:x/ HTTP/1.1\r\n", "not a host")]
    [InlineData("GET http://example.com/ HTTP/1.1 extra\r\n", "request line")]
    [InlineData("GET http://example.com/\r\n", "request line")]
    [InlineData("GET http://example.com/ SPDY/3\r\n", "request line")]
    [InlineData("G(ET http://example.com/ HTTP/1.1\r\n", "request line")]
    [InlineData("GET http://example.com/ HTTP/1.1\r\nBad header\r\n", "not well formed")]
    [InlineData("GET http://example.com/ HTTP/1.1\r\n: value\r\n", "not well formed")]
    [InlineData("GET http://example.com/ HTTP/1.1\r\nX: a\r\n continued\r\n", "folded")]
    [InlineData("GET http://example.com/ HTTP/1.1\r\nX: a\nY: b\r\n", "bare line break")]
    [InlineData("GET http://example.com/ HTTP/1.1\r\nContent-Length: 1\r\nTransfer-Encoding: chunked\r\n", "stated twice")]
    [InlineData("GET http://example.com/ HTTP/1.1\r\nContent-Length: 1\r\nContent-Length: 2\r\n", "stated twice")]
    [InlineData("GET http://example.com/x\ty HTTP/1.1\r\n", "request line cannot")]
    [InlineData("GET http://example.com/x\u00e9 HTTP/1.1\r\n", "request line cannot")]
    public void What_the_proxy_cannot_carry_is_refused_with_the_reason(string head, string reason)
    {
        var (request, problem) = Parse(head);

        Assert.Null(request);
        Assert.Contains(reason, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tunnel_with_a_body_length_stated_twice_is_still_a_tunnel_because_nothing_of_it_is_read()
    {
        var (request, _) = Parse("CONNECT example.com:443 HTTP/1.1\r\nContent-Length: 1\r\nTransfer-Encoding: chunked\r\n");

        Assert.True(request!.IsTunnel);
    }
}
