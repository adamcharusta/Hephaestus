using Docker.DotNet;
using Docker.DotNet.Models;

namespace Hephaestus.Workspaces;

internal static class ContainerLogs
{
    /// <summary>What a container has written so far to its standard output and error, as one text. It does not follow the log, so it returns at once.</summary>
    public static async Task<string> ReadAsync(IDockerClient docker, string container, CancellationToken cancellationToken)
    {
        using var logs = await docker.Containers.GetContainerLogsAsync(container, new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Follow = false }, cancellationToken);
        var (output, error) = await logs.ReadOutputToEndAsync(cancellationToken);
        return output + error;
    }
}
