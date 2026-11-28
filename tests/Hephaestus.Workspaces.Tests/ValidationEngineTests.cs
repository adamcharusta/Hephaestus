using System.Runtime.Versioning;
using System.Security.Cryptography;
using Docker.DotNet.Models;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.12 against a real Docker Engine (AD-6, AD-13, FR-31): a Validation command runs through bash on a fresh checkout of the tip, as the step user, with the Task volume as its only mount, and what it leaves in the tree
/// is gone when the next command is checked out. The tests are skipped where no Engine answers and run in CI, which names the tools image and the <c>hephaestus</c> image for the run.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ValidationEngineTests(EngineFixture engine) : IClassFixture<EngineFixture>
{
    private const string Branch = EngineWorkspaces.Branch;
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<(ResolvedImage Image, Guid TaskId, string Repository, string Tip)> PrepareAsync()
    {
        EngineWorkspaces.Unless(engine.ToolsImage is not null, $"{EngineFixture.ToolsImageVariable} names no hephaestus-tools image to create the checkout containers from.");
        EngineWorkspaces.Unless(engine.ProxyImage is not null, $"{EngineFixture.ProxyImageVariable} names no hephaestus image to create the egress proxies from.");
        EngineWorkspaces.Unless(LocalGit.Available, "No git and shell on this machine to make the repository the bundle is made of.");

        // The Project image contract asks for bash, which a minimal musl image lacks.
        var image = await engine.Runtime.ResolveImageAsync(EngineFixture.Glibc, Cancel);
        var taskId = Guid.CreateVersion7();
        await engine.Runtime.EnsureTaskVolumeAsync(taskId, image, "task:v1:volume:0", Cancel);
        var repository = Path.Combine(Path.GetTempPath(), $"hephaestus-validation-source-{Guid.NewGuid():N}");
        return (image, taskId, repository, LocalGit.Repository(repository));
    }

    private async Task<StartedStep> StartAsync(Guid taskId, ResolvedImage image, string repository, string tip, string command)
    {
        var step = Guid.CreateVersion7();
        var name = $"{step:D}/{step:D}-validation.bundle";
        var path = Path.Combine(engine.BundlesDirectory, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        LocalGit.Bundle(repository, path, "main");
        string checksum;
        await using (var file = File.OpenRead(path))
        {
            checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, Cancel));
        }

        var request = new ValidationCommandRequest(taskId, step, Branch, new BundleReference(name, checksum, tip), image, command, new StepLimits(1500, 512L * 1024 * 1024), [], DateTimeOffset.UtcNow.AddMinutes(10));
        return await engine.Runtime.StartValidationAsync(request, $"task:v1:validation:{step:D}", Cancel);
    }

    private async Task<(long ExitCode, string Output)> RunToEndAsync(string container)
    {
        var wait = await engine.Docker.Containers.WaitContainerAsync(container, Cancel);
        using var logs = await engine.Docker.Containers.GetContainerLogsAsync(container, new ContainerLogsParameters { ShowStdout = true, ShowStderr = true }, Cancel);
        var (output, error) = await logs.ReadOutputToEndAsync(Cancel);
        return (wait.StatusCode, output + error);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_command_runs_through_bash_on_the_checked_out_tip_as_the_step_user_and_its_exit_status_is_the_containers()
    {
        var (image, taskId, repository, tip) = await PrepareAsync();

        var started = await StartAsync(taskId, image, repository, tip, "cat README.md && echo \"user $(id -u)\" && echo built > built.txt && [[ -f built.txt ]] && exit 3");
        var (exit, output) = await RunToEndAsync(started.ContainerId);

        Assert.Equal(3, exit);
        Assert.Contains("hello", output, StringComparison.Ordinal);
        Assert.Contains("user 10001", output, StringComparison.Ordinal);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task What_a_command_left_in_the_tree_is_gone_when_the_next_command_is_checked_out()
    {
        var (image, taskId, repository, tip) = await PrepareAsync();
        var first = await StartAsync(taskId, image, repository, tip, "echo built > built.txt && mkdir -p node_modules/.bin && touch node_modules/.bin/jest");
        Assert.Equal(0, (await RunToEndAsync(first.ContainerId)).ExitCode);

        var second = await StartAsync(taskId, image, repository, tip, "test ! -e built.txt && test ! -e node_modules && test -f README.md");
        var (exit, output) = await RunToEndAsync(second.ContainerId);

        Assert.True(exit == 0, output);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_container_mounts_the_Task_volume_and_nothing_else_and_holds_no_credential()
    {
        var (image, taskId, repository, tip) = await PrepareAsync();

        var started = await StartAsync(taskId, image, repository, tip, "exit 0");
        await RunToEndAsync(started.ContainerId);
        var inspected = await engine.Docker.Containers.InspectContainerAsync(started.ContainerId, Cancel);

        var mounts = inspected.Mounts!.Where(mount => mount.Type != "tmpfs").ToList();
        var mount = Assert.Single(mounts);
        Assert.Equal("/workspace", mount.Destination);
        Assert.Equal($"hephaestus-ws-{taskId}", mount.Name);
        Assert.Equal(["bash"], inspected.Config!.Entrypoint);
        Assert.DoesNotContain(inspected.Config.Env!, variable => variable.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || variable.Contains("API_KEY", StringComparison.OrdinalIgnoreCase));
    }
}
