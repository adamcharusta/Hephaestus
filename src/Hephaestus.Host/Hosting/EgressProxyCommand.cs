using System.Net;
using System.Runtime.InteropServices;
using Hephaestus.Workspaces.Egress;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// <c>hephaestus egress-proxy</c>: the egress proxy of one step run (AD-7, Story 3.4). The runtime creates a container of the <c>hephaestus</c> image with this command, so the proxy is the same
/// release as the orchestrator and a process of its own. It reads one thing, the step run's list of allowed hosts, from the environment; it opens no database, reads no secret, and touches no
/// file. Its log is the channel to the runtime: the line that says it is listening, and a line for each host it refused.
/// </summary>
internal static class EgressProxyCommand
{
    public static async Task<int> RunAsync(Func<string, string?> getVariable, TextWriter log, int port, CancellationToken cancellationToken)
    {
        var allowlist = EgressAllowlist.Parse(getVariable(EgressAllowlist.VariableName));
        await using var server = new EgressProxyServer(allowlist, new EgressProxyOptions(), line =>
        {
            lock (log)
            {
                log.WriteLine(line);
                log.Flush();
            }
        });
        server.Start(new IPEndPoint(IPAddress.Any, port));

        // The container is stopped with SIGTERM; it is removed when the step ends.
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            stopping.Cancel();
        });
        await server.RunAsync(stopping.Token);
        return 0;
    }
}
