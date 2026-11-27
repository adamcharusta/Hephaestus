using Hephaestus.Core.Localization;

namespace Hephaestus.Workspaces;

/// <summary>
/// The agent binaries volume could not be brought to the state the lock demands (AD-8). Startup stops on it; the message names the file
/// that failed and is for the Owner, who reads it in the container's log.
/// </summary>
public sealed class AgentBinaryException(Message info, Exception? innerException = null) : Exception(info.Id, innerException)
{
    public Message Info { get; } = info;
}
