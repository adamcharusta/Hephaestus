using System.Security.Cryptography;
using Docker.DotNet.Models;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>What the tests that run the tools image on a real Engine share (Stories 3.5 and 3.6): a Task volume, a repository to make bundles from, a checkout, and a command run as the step user on the volume.</summary>
internal sealed class EngineWorkspaces(EngineFixture engine)
{
    public const string Branch = "hephaestus/12-fresh-checkout";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public sealed record Made(Guid TaskId, ResolvedImage Image, string Repository, string Tip);

    public async Task<Made> PrepareAsync()
    {
        Unless(engine.ToolsImage is not null, $"{EngineFixture.ToolsImageVariable} names no hephaestus-tools image to create the checkout containers from.");
        Unless(LocalGit.Available, "No git and shell on this machine to make the repository the bundle is made of.");
        var image = await engine.Runtime.ResolveImageAsync(EngineFixture.Musl, Cancel);
        var taskId = Guid.CreateVersion7();
        await engine.Runtime.EnsureTaskVolumeAsync(taskId, image, "task:c1:volume:0", Cancel);
        var repository = Path.Combine(Path.GetTempPath(), $"hephaestus-checkout-source-{Guid.NewGuid():N}");
        var tip = LocalGit.Repository(repository);
        return new Made(taskId, image, repository, tip);
    }

    public async Task CheckoutAsync(Made made, Guid? stepRunId = null, string? tip = null)
    {
        var step = stepRunId ?? Guid.CreateVersion7();
        var name = $"{step:D}/{step:D}-checkout.bundle";
        var path = Path.Combine(engine.BundlesDirectory, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        LocalGit.Bundle(made.Repository, path, "main");
        string checksum;
        await using (var file = File.OpenRead(path))
        {
            checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, Cancel));
        }

        await engine.Runtime.CheckoutAsync(new CheckoutRequest(made.TaskId, step, Branch, new BundleReference(name, checksum, tip ?? made.Tip)), $"task:c1:checkout:{step:D}", Cancel);
    }

    /// <summary>Runs a shell command as the step user in a container that mounts the Task volume as a step container does, and returns what it printed. <paramref name="image"/> is the image it runs in.</summary>
    public async Task<(long ExitCode, string Output)> RunAsync(Made made, string script, string? image = null)
    {
        var name = $"hephaestus-probe-{Guid.NewGuid():N}";
        var created = await engine.Docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = name,
                Image = image ?? made.Image.Reference,
                User = StepContainerLayout.User,
                Entrypoint = ["sh", "-c", script],
                Labels = new Dictionary<string, string> { ["hephaestus.installation"] = engine.InstallationId, ["hephaestus.task"] = made.TaskId.ToString(), ["hephaestus.step-run"] = string.Empty },
                HostConfig = new HostConfig
                {
                    NetworkMode = "none",
                    CapDrop = ["ALL"],
                    Mounts = [new Mount { Type = "volume", Source = $"hephaestus-ws-{made.TaskId}", Target = "/workspace", VolumeOptions = new VolumeOptions { NoCopy = true } }],
                },
            },
            Cancel);
        await engine.Docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), Cancel);
        var wait = await engine.Docker.Containers.WaitContainerAsync(created.ID, Cancel);
        using var logs = await engine.Docker.Containers.GetContainerLogsAsync(created.ID, new ContainerLogsParameters { ShowStdout = true, ShowStderr = true }, Cancel);
        var (output, error) = await logs.ReadOutputToEndAsync(Cancel);
        return (wait.StatusCode, (output + error).Trim());
    }

    public static void Unless(bool condition, string reason)
    {
        if (condition)
        {
            return;
        }

        if (RealEngine.Required)
        {
            Assert.Fail(reason);
        }

        Assert.Skip(reason);
    }
}
