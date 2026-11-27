using System.Runtime.InteropServices;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;
using Hephaestus.Workspaces;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// The startup step that fills the agent binaries volume (AD-8, AR-40): after the database is migrated and before the loop's first tick, it
/// reads the lock the image carries, downloads what the volume lacks from each vendor's official channel, verifies every executable, and
/// places the CA bundle of the image's own certificate store. Anything it cannot establish stops startup with a sentence that names the file.
/// </summary>
internal static class AgentBinaryStartup
{
    /// <summary>The sentence that says why startup stopped, in the installation's language, or <see langword="null"/> when the volume is ready.</summary>
    public static async Task<string?> PrepareAsync(HostSettings settings, ILogger log, CancellationToken cancellationToken)
    {
        var catalog = MessageCatalog.Instance;
        string Say(Message message) => catalog.Resolve(message, settings.Language);

        AgentLock agentLock;
        try
        {
            agentLock = AgentLock.Parse(await File.ReadAllTextAsync(settings.AgentsLockFile, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Say(new Message(MessageIds.AgentsStartLockUnreadable, settings.AgentsLockFile, exception.Message));
        }
        catch (AgentLockException exception)
        {
            return Say(new Message(MessageIds.AgentsStartLockInvalid, settings.AgentsLockFile, Say(exception.Info)));
        }

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => (AgentArchitecture?)AgentArchitecture.X64,
            Architecture.Arm64 => AgentArchitecture.Arm64,
            _ => null,
        };
        if (architecture is not { } supported)
        {
            return Say(new Message(MessageIds.AgentsStartArchitecture, RuntimeInformation.ProcessArchitecture.ToString()));
        }

        using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(30) };
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"hephaestus/{Core.HephaestusVersion.Current}");
        try
        {
            await new AgentBinaryVolume(http).PrepareAsync(
                agentLock,
                supported,
                settings.AgentsDirectory,
                settings.CaBundleFile,
                cancellationToken,
                result => log.LogInformation(
                    "Agent executable {Path} {Version}: {Outcome}.",
                    result.Binary.RelativePath,
                    result.Binary.Version,
                    result.Fetched ? "downloaded and verified" : "already in the volume and verified"));

            return null;
        }
        catch (AgentBinaryException exception)
        {
            return Say(exception.Info);
        }
    }
}
