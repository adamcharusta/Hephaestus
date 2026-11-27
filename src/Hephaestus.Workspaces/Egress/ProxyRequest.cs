using System.Globalization;
using System.Text;

namespace Hephaestus.Workspaces.Egress;

/// <summary>What the proxy was asked for: a tunnel to a host and port, or one plain HTTP request to an origin.</summary>
internal sealed record ProxyRequest(bool IsTunnel, string Host, int Port, string Method, string Path, IReadOnlyList<KeyValuePair<string, string>> Headers)
{
    /// <summary>The headers that belong to one connection, not to the request: the proxy writes its own, so a client cannot choose how the origin frames or keeps the connection.</summary>
    private static readonly HashSet<string> ConnectionHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Proxy-Connection", "Proxy-Authorization", "Proxy-Authenticate", "Keep-Alive", "TE", "Trailer", "Upgrade", "Host",
    };

    /// <summary>
    /// Reads the head of a request, which is everything before the empty line. It accepts what a proxy can carry without reading the body, and refuses the rest with the reason:
    /// a head that is not well formed, a header folded onto the next line, a bare line feed, and a body whose length is stated twice, which are how one request is made to look like two.
    /// </summary>
    public static (ProxyRequest? Request, string? Problem) Parse(ReadOnlySpan<byte> head)
    {
        // The head is what came before the empty line; a trailing line break, which a client may send on its own, is not a line.
        var text = Encoding.Latin1.GetString(head).TrimEnd('\r', '\n');
        var lines = text.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal) || parts[0].Length == 0 || !parts[0].All(IsTokenCharacter))
        {
            return (null, "the request line is not well formed");
        }

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Contains('\r') || line.Contains('\n') || line.Contains('\0'))
            {
                return (null, "a header holds a bare line break");
            }

            if (line.Length > 0 && line[0] is ' ' or '\t')
            {
                return (null, "a header is folded onto the next line");
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 1 || !line[..colon].All(IsTokenCharacter))
            {
                return (null, "a header is not well formed");
            }

            headers.Add(new(line[..colon], line[(colon + 1)..].Trim(' ', '\t')));
        }

        var method = parts[0];
        var target = parts[1];
        if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
        {
            return TryAuthority(target, defaultPort: null, out var tunnelHost, out var tunnelPort)
                ? (new ProxyRequest(true, tunnelHost, tunnelPort, method, string.Empty, headers), null)
                : (null, "the tunnel's destination is not a host and a port");
        }

        if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "a request to the proxy names a destination as an http address");
        }

        // A fragment is the client's own and never sent; a path with a control character could end the request line early at the origin.
        var rest = target["http://".Length..];
        var hash = rest.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            rest = rest[..hash];
        }

        var end = rest.IndexOfAny(['/', '?']);
        var authority = end < 0 ? rest : rest[..end];
        var path = end < 0 ? "/" : rest[end..];
        if (path.StartsWith('?'))
        {
            path = "/" + path;
        }

        if (path.Any(c => c <= ' ' || c >= (char)0x7F))
        {
            return (null, "the path holds a character a request line cannot");
        }

        if (!TryAuthority(authority, defaultPort: 80, out var host, out var port) || authority.Contains('@', StringComparison.Ordinal))
        {
            return (null, "the destination is not a host and a port");
        }

        if (headers.Any(h => h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) && headers.Any(h => h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)))
        {
            return (null, "the length of the body is stated twice");
        }

        if (headers.Count(h => h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) > 1)
        {
            return (null, "the length of the body is stated twice");
        }

        return (new ProxyRequest(false, host, port, method, path, headers), null);
    }

    /// <summary>
    /// The request as the origin receives it: origin form, the host the proxy checked in the Host header, the connection closed after the response, and the rest of the headers as the
    /// client sent them. Closing the connection after one response means a second request pipelined behind the first is never read as one.
    /// </summary>
    public byte[] ToOriginForm()
    {
        var builder = new StringBuilder();
        builder.Append(Method).Append(' ').Append(Path).Append(" HTTP/1.1\r\n");
        builder.Append("Host: ").Append(Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]" : Host);
        if (Port != 80)
        {
            builder.Append(':').Append(Port.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append("\r\n");
        foreach (var header in Headers.Where(header => !ConnectionHeaders.Contains(header.Key)))
        {
            builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        }

        builder.Append("Connection: close\r\n\r\n");
        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    /// <summary>A host, with a port that the tunnel's target must have and a plain request's may not, or an IPv6 address in brackets.</summary>
    private static bool TryAuthority(string authority, int? defaultPort, out string host, out int port)
    {
        host = string.Empty;
        port = defaultPort ?? 0;
        var portText = string.Empty;
        if (authority.StartsWith('['))
        {
            var close = authority.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }

            host = authority[1..close];
            var after = authority[(close + 1)..];
            if (after.Length > 0)
            {
                if (after[0] != ':')
                {
                    return false;
                }

                portText = after[1..];
            }
            else if (defaultPort is null)
            {
                return false;
            }
        }
        else
        {
            var colon = authority.LastIndexOf(':');
            if (colon >= 0)
            {
                host = authority[..colon];
                portText = authority[(colon + 1)..];
            }
            else if (defaultPort is null)
            {
                return false;
            }
            else
            {
                host = authority;
            }
        }

        if (portText.Length > 0 && !(int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535))
        {
            return false;
        }

        host = EgressAllowlist.Normalize(host);
        return host.Length > 0 && host.Length <= EgressRefusals.MaximumHostLength && !host.Any(c => c is <= ' ' or '/' or '\\' or '@' or '?' or '#');
    }

    private static bool IsTokenCharacter(char c) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c, StringComparison.Ordinal);
}
