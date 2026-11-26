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
        TextWriter output,
        TextWriter error,
        Func<string, string?> getVariable,
        CancellationToken cancellationToken)
    {
        var settings = HostSettings.FromEnvironment(getVariable);

        if (args is ["serve"])
        {
            await using var app = ControlHost.Build(settings);
            await app.RunAsync(cancellationToken);
            return 0;
        }

        using var client = new ControlApiClient(settings.ControlSocketPath, settings.Language);
        return await new CliApplication(settings, client, output, error).RunAsync(args, cancellationToken);
    }
}
