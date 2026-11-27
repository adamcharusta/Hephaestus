using Hephaestus.Core.Workspaces;
using Hephaestus.Host.Cli;
using Hephaestus.Host.Hosting;

namespace Hephaestus.Host;

/// <summary>
/// One executable, two modes (AD-17): <c>hephaestus serve</c> is the long-running host, every other invocation is the
/// command line, a client of the control API. <c>serve</c> is the container's process, not a command of the tree.
/// </summary>
internal static class EntryPoint
{
    public static async Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        Func<string, string?> getVariable,
        CancellationToken cancellationToken)
    {
        // The egress proxy of a step run is this executable in another container (Story 3.4): it needs no settings, no database, and no control API.
        if (args is ["egress-proxy"])
        {
            return await EgressProxyCommand.RunAsync(getVariable, output, EgressProxyAddress.Port, cancellationToken);
        }

        var settings = HostSettings.FromEnvironment(getVariable);

        if (args is ["serve"])
        {
            await using var app = ControlHost.Build(settings);
            await ControlHost.MigrateAsync(app, cancellationToken);
            // The loop's first tick is the host's start: nothing runs until the Agent executables are in the volume and verified (AR-40).
            var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Hephaestus.Host");
            if (await AgentBinaryStartup.PrepareAsync(settings, log, cancellationToken) is { } stopped)
            {
                await error.WriteLineAsync(stopped);
                return 1;
            }

            await app.RunAsync(cancellationToken);
            return 0;
        }

        using var client = new ControlApiClient(settings.ControlSocketPath, settings.Language);
        return await new CliApplication(settings, client, input, output, error).RunAsync(args, cancellationToken);
    }
}
