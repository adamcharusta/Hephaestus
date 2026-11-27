using System.Net;
using System.Net.Sockets;
using System.Text;
using Hephaestus.Host.Hosting;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>Story 3.4, AR-41: <c>hephaestus egress-proxy</c> is the proxy of one step run, configured by its environment alone, reporting through its standard output.</summary>
public class EgressProxyCommandTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<int> ListeningPortAsync(StringWriter output)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var line = output.ToString().Split('\n').FirstOrDefault(line => line.StartsWith("listening ", StringComparison.Ordinal));
            if (line is not null)
            {
                return int.Parse(line["listening ".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }

            await Task.Delay(50, Cancel);
        }

        throw new TimeoutException("The proxy did not say it was listening.");
    }

    private static async Task<string> SendAsync(int port, string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, Cancel);
        using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), Cancel);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(Cancel);
    }

    [Fact]
    public async Task The_command_listens_says_so_on_its_output_applies_the_list_from_its_environment_and_ends_when_it_is_stopped()
    {
        using var output = new StringWriter();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        var running = EgressProxyCommand.RunAsync(name => name == "HEPHAESTUS_EGRESS_ALLOWED_HOSTS" ? "api.nuget.org" : null, output, 0, stopping.Token);
        var port = await ListeningPortAsync(output);

        var refused = await SendAsync(port, "CONNECT example.com:443 HTTP/1.1\r\n\r\n");
        var loopback = await SendAsync(port, "CONNECT api.nuget.org:443 HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403 Forbidden", refused, StringComparison.Ordinal);
        Assert.Contains("refused example.com not-allowed", output.ToString(), StringComparison.Ordinal);
        // api.nuget.org is on the list, so the proxy resolves it: the result depends on the network of this machine and is not asserted beyond being an answer of the proxy.
        Assert.StartsWith("HTTP/1.1 ", loopback, StringComparison.Ordinal);

        await stopping.CancelAsync();
        Assert.Equal(0, await running.WaitAsync(TimeSpan.FromSeconds(10), Cancel));
    }

    [Fact]
    public async Task Without_the_variable_the_command_applies_no_list_and_a_private_address_is_still_refused()
    {
        using var output = new StringWriter();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        var running = EgressProxyCommand.RunAsync(_ => null, output, 0, stopping.Token);
        var port = await ListeningPortAsync(output);

        var answer = await SendAsync(port, "CONNECT 127.0.0.1:8717 HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403 Forbidden", answer, StringComparison.Ordinal);
        Assert.Contains("refused 127.0.0.1 private-address", output.ToString(), StringComparison.Ordinal);
        await stopping.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
    }
}
