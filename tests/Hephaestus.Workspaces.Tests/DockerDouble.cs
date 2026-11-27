using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using NSubstitute;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// A Docker Engine in memory, behind <see cref="IDockerClient"/>: it records what the runtime asks for and answers the way the Engine does for what
/// was set up. The tests that run against a real Engine are <see cref="DockerEngineTests"/>.
/// </summary>
internal sealed class DockerDouble
{
    public DockerDouble()
    {
        Client = Substitute.For<IDockerClient>();
        Client.Containers.CreateContainerAsync(Arg.Any<CreateContainerParameters>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var parameters = call.Arg<CreateContainerParameters>();
            Calls.Add($"create container {parameters.Name}");
            if (FailCreate?.Invoke(parameters) is { } failure)
            {
                throw failure;
            }

            Created.Add(parameters);
            return Task.FromResult(new CreateContainerResponse { ID = $"id-{parameters.Name}" });
        });
        Client.Containers.StartContainerAsync(Arg.Any<string>(), Arg.Any<ContainerStartParameters>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Calls.Add($"start {call.Arg<string>()}");
            Started.Add(call.Arg<string>());
            return StartFails is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(true);
        });
        Client.Containers.RemoveContainerAsync(Arg.Any<string>(), Arg.Any<ContainerRemoveParameters>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Calls.Add($"remove {call.Arg<string>()}");
            Removed.Add(call.Arg<string>());
            Removals.Add(call.Arg<ContainerRemoveParameters>());
            return RemoveFails?.Invoke(call.Arg<string>()) is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        });
        Client.Containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Listings.Add(call.Arg<ContainersListParameters>());
            return Task.FromResult<IList<ContainerListResponse>>([.. Existing]);
        });
        Client.Containers.ExtractArchiveToContainerAsync(Arg.Any<string>(), Arg.Any<CopyToContainerParameters>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            using var copy = new MemoryStream();
            call.Arg<Stream>().CopyTo(copy);
            Calls.Add($"extract {call.Arg<string>()} {call.Arg<CopyToContainerParameters>().Path}");
            Extractions.Add(new Extraction(call.Arg<string>(), call.Arg<CopyToContainerParameters>(), copy.ToArray()));
            return ExtractFails is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        });
        Client.Containers.GetArchiveFromContainerAsync(Arg.Any<string>(), Arg.Any<ContainerPathStatParameters>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var path = call.Arg<ContainerPathStatParameters>().Path;
            Statted.Add(path);
            return ImagePaths.Contains(path)
                ? Task.FromResult(new ContainerArchiveResponse { Stat = new ContainerPathStatResponse(), Stream = new MemoryStream() })
                : Task.FromException<ContainerArchiveResponse>(NotFound());
        });
        Client.Containers.InspectContainerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.Arg<string>();
            Calls.Add($"inspect {id}");
            return Inspector(id, ++InspectCalls);
        });
        Client.Containers.GetContainerLogsAsync(Arg.Any<string>(), Arg.Any<ContainerLogsParameters>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Calls.Add($"logs {call.Arg<string>()}");
            LogRequests.Add(call.Arg<ContainerLogsParameters>());
            return Logs(call.Arg<string>(), call.Arg<CancellationToken>());
        });
        Client.Containers.StopContainerAsync(Arg.Any<string>(), Arg.Any<ContainerStopParameters>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Calls.Add($"stop {call.Arg<string>()}");
            Stops.Add(call.Arg<ContainerStopParameters>());
            return StopFails?.Invoke(call.Arg<string>()) is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(true);
        });
        Client.Volumes.RemoveAsync(Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Calls.Add($"remove volume {call.Arg<string>()}");
            RemovedVolumes.Add(call.Arg<string>());
            return VolumeRemoveFails?.Invoke(call.Arg<string>()) is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        });
        Client.Volumes.CreateAsync(Arg.Any<VolumesCreateParameters>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var parameters = call.Arg<VolumesCreateParameters>();
            Calls.Add($"create volume {parameters.Name}");
            Volumes.Add(parameters);
            return Task.FromResult(new VolumeResponse { Name = parameters.Name });
        });
        Client.Networks.InspectNetworkAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            NetworkExists ? Task.FromResult(new NetworkResponse { Name = call.Arg<string>(), Internal = NetworkInternal }) : Task.FromException<NetworkResponse>(NotFound()));
        Client.Networks.CreateNetworkAsync(Arg.Any<NetworksCreateParameters>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Calls.Add($"create network {call.Arg<NetworksCreateParameters>().Name}");
            Networks.Add(call.Arg<NetworksCreateParameters>());
            return NetworkCreateFails is { } failure ? Task.FromException<NetworksCreateResponse>(failure) : Task.FromResult(new NetworksCreateResponse { ID = "network-id" });
        });
        Client.Images.InspectImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Calls.Add($"inspect image {call.Arg<string>()}");
            return Image is { } image ? Task.FromResult(image) : Task.FromException<ImageInspectResponse>(NotFound());
        });
        Client.Images.CreateImageAsync(Arg.Any<ImagesCreateParameters>(), Arg.Any<AuthConfig>(), Arg.Any<IProgress<JSONMessage>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Calls.Add($"pull {call.Arg<ImagesCreateParameters>().FromImage}");
            Pulls.Add(call.Arg<ImagesCreateParameters>());
            foreach (var message in PullMessages)
            {
                call.Arg<IProgress<JSONMessage>>().Report(message);
            }

            if (PullFails is { } failure)
            {
                return Task.FromException(failure);
            }

            Image ??= PulledImage;
            return Task.CompletedTask;
        });
    }

    public IDockerClient Client { get; }

    /// <summary>What the Engine says of a container when asked, given the container and how many times it has been asked: running, until a test says otherwise.</summary>
    public Func<string, int, Task<ContainerInspectResponse>> Inspector { get; set; } = (_, _) => Task.FromResult(Inspected("running"));

    /// <summary>The output of a container: nothing, which ends at once.</summary>
    public Func<string, CancellationToken, Task<MultiplexedStream>> Logs { get; set; } = (_, _) => Task.FromResult(Output());

    public int InspectCalls { get; private set; }

    public List<ContainerLogsParameters> LogRequests { get; } = [];

    public List<ContainerStopParameters> Stops { get; } = [];

    public List<string> RemovedVolumes { get; } = [];

    public Func<string, Exception?>? StopFails { get; set; }

    public Func<string, Exception?>? VolumeRemoveFails { get; set; }

    public static ContainerInspectResponse Inspected(string status, long exitCode = 0, bool oomKilled = false) =>
        new() { State = new() { Status = status, Running = status == "running", ExitCode = exitCode, OOMKilled = oomKilled } };

    /// <summary>Output as the Engine frames it for a container with no terminal: a header of the stream and the size, then the bytes. Stream 1 is standard output, 2 is standard error.</summary>
    public static byte[] Frames(params (int Stream, string Text)[] frames)
    {
        var buffer = new MemoryStream();
        foreach (var (stream, text) in frames)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var header = new byte[8];
            header[0] = (byte)stream;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)bytes.Length);
            buffer.Write(header);
            buffer.Write(bytes);
        }

        return buffer.ToArray();
    }

    public static MultiplexedStream Output(params (int Stream, string Text)[] frames) => new(new MemoryStream(Frames(frames)), multiplexed: true);

    /// <summary>Everything the runtime did, in order, one short line each.</summary>
    public List<string> Calls { get; } = [];

    public List<CreateContainerParameters> Created { get; } = [];

    public List<string> Started { get; } = [];

    public List<string> Removed { get; } = [];

    public List<ContainersListParameters> Listings { get; } = [];

    public List<Extraction> Extractions { get; } = [];

    public List<VolumesCreateParameters> Volumes { get; } = [];

    public List<NetworksCreateParameters> Networks { get; } = [];

    public List<ImagesCreateParameters> Pulls { get; } = [];

    public List<string> Statted { get; } = [];

    /// <summary>The containers the Engine already holds, as a list shows them.</summary>
    public List<ContainerListResponse> Existing { get; } = [];

    /// <summary>The paths that exist in the image a container is created from.</summary>
    public HashSet<string> ImagePaths { get; } = [];

    public bool NetworkExists { get; set; }

    public bool NetworkInternal { get; set; } = true;

    /// <summary>What a pull says in its stream.</summary>
    public List<JSONMessage> PullMessages { get; } = [];

    public List<ContainerRemoveParameters> Removals { get; } = [];

    public Exception? StartFails { get; set; }

    public ImageInspectResponse? Image { get; set; }

    /// <summary>What a pull leaves on the machine when the image was not there.</summary>
    public ImageInspectResponse? PulledImage { get; set; }

    public Exception? PullFails { get; set; }

    public Exception? ExtractFails { get; set; }

    public Exception? NetworkCreateFails { get; set; }

    public Func<string, Exception?>? RemoveFails { get; set; }

    public Func<CreateContainerParameters, Exception?>? FailCreate { get; set; }

    public static DockerApiException NotFound() => new(HttpStatusCode.NotFound, "no such object");

    public static DockerApiException Conflict() => new(HttpStatusCode.Conflict, "already in use");

    public static ImageInspectResponse ImageOf(string id, string architecture, params string[] repoDigests) =>
        new() { ID = id, Architecture = architecture, RepoDigests = repoDigests };

    public sealed record Extraction(string Container, CopyToContainerParameters Parameters, byte[] Tar);
}
