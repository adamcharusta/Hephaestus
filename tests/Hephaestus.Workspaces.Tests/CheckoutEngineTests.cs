using System.Runtime.Versioning;
using System.Security.Cryptography;
using Docker.DotNet.Models;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.5 against a real Docker Engine and the real tools image (AD-6, AD-9, FR-17, FR-18, AR-49): the checkout re-creates <c>/workspace</c> from a bundle as the step user, and a hook, a configuration entry,
/// and an ignored fake runner that a step planted are gone after the next one. The tests are skipped where no Engine answers and run in CI, where the tools image built for the run is named by
/// <see cref="EngineFixture.ToolsImageVariable"/>.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class CheckoutEngineTests(EngineFixture engine) : IClassFixture<EngineFixture>
{
    private const string Branch = "hephaestus/12-fresh-checkout";
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private sealed record Made(Guid TaskId, ResolvedImage Image, string Repository, string Tip);

    private async Task<Made> PrepareAsync()
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

    private async Task CheckoutAsync(Made made, Guid? stepRunId = null, string? tip = null)
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
    private async Task<(long ExitCode, string Output)> RunAsync(Made made, string script, string? image = null)
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

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_workspace_is_a_fresh_clone_of_the_tip_on_the_task_branch_with_default_configuration_and_no_hook_or_remote()
    {
        var made = await PrepareAsync();

        await CheckoutAsync(made);

        var (exit, output) = await RunAsync(made, "cd /workspace && git rev-parse HEAD && git symbolic-ref --short HEAD && git status --porcelain --ignored && echo --- && git config --local --list && echo --- && ls -A .git/hooks 2>&1; git remote", engine.ToolsImage);
        Assert.Equal(0, exit);
        var parts = output.Split("---", StringSplitOptions.TrimEntries);
        Assert.Equal([made.Tip, Branch], parts[0].Split('\n', StringSplitOptions.TrimEntries));
        Assert.Equal(["core.bare=false", "core.filemode=true", "core.logallrefupdates=true", "core.repositoryformatversion=0"], parts[1].Split('\n', StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal));
        Assert.Contains("No such file or directory", parts[2], StringComparison.Ordinal);
        Assert.DoesNotContain("url", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_hook_a_configuration_entry_and_an_ignored_fake_runner_a_step_planted_do_not_exist_after_the_next_checkout()
    {
        var made = await PrepareAsync();
        await CheckoutAsync(made);

        var plant = "cd /workspace && mkdir -p .git/hooks node_modules/.bin locked/inner"
            + " && printf '#!/bin/sh\\nexit 0\\n' > .git/hooks/post-checkout && chmod +x .git/hooks/post-checkout"
            + " && printf '[core]\\n\\tfsmonitor = true\\n' >> .git/config"
            + " && printf '#!/bin/sh\\nexit 0\\n' > node_modules/.bin/jest && chmod +x node_modules/.bin/jest"
            + " && echo left > untracked.txt && echo x > locked/inner/f && chmod 000 locked/inner locked";
        Assert.Equal(0, (await RunAsync(made, plant)).ExitCode);
        await CheckoutAsync(made);

        var (exit, output) = await RunAsync(made, "cd /workspace && ls -A .git/hooks 2>&1; grep -c fsmonitor .git/config; ls -A node_modules untracked.txt locked 2>&1; git status --porcelain --ignored", engine.ToolsImage);
        Assert.Equal(0, exit);
        Assert.Contains("No such file or directory", output, StringComparison.Ordinal);
        Assert.DoesNotContain("post-checkout", output, StringComparison.Ordinal);
        Assert.DoesNotContain("jest", output, StringComparison.Ordinal);
        Assert.Equal(1, output.Split('\n').Count(line => line.StartsWith('0')));
        Assert.DoesNotContain("??", output, StringComparison.Ordinal);
        Assert.DoesNotContain("!!", output, StringComparison.Ordinal);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_step_container_can_write_to_every_path_the_checkout_created()
    {
        var made = await PrepareAsync();
        await CheckoutAsync(made);

        var (exit, output) = await RunAsync(
            made,
            "cd /workspace && test -z \"$(find . ! -user 10001)\" && find . -type d | while read d; do touch \"$d/.probe\" && rm \"$d/.probe\" || exit 1; done && find . -type f | while read f; do echo >> \"$f\" || exit 1; done && echo written");
        Assert.True(exit == 0, output);
        Assert.Equal("written", output);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_checkout_container_is_removed_when_it_ends_and_so_is_the_bundle_and_no_volume_but_the_tasks_exists()
    {
        var made = await PrepareAsync();
        var step = Guid.CreateVersion7();

        await CheckoutAsync(made, step);

        Assert.Empty(await engine.Docker.Containers.ListContainersAsync(new ContainersListParameters { All = true, Filters = new Dictionary<string, IDictionary<string, bool>> { ["name"] = new Dictionary<string, bool> { [$"hephaestus-checkout-{step}"] = true } } }, Cancel));
        Assert.False(File.Exists(Path.Combine(engine.BundlesDirectory, $"{step}", $"{step}-checkout.bundle")));
        var volumes = (await engine.Docker.Volumes.ListAsync(new VolumesListParameters { Filters = new Dictionary<string, IDictionary<string, bool>> { ["label"] = new Dictionary<string, bool> { [$"hephaestus.task={made.TaskId}"] = true } } }, Cancel)).Volumes;
        Assert.Equal([$"hephaestus-ws-{made.TaskId}"], volumes!.Select(volume => volume.Name));
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_tip_that_is_not_the_one_in_the_bundle_fails_the_checkout_with_the_message_of_that_refusal()
    {
        var made = await PrepareAsync();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(made, tip: new string('a', 40)));

        Assert.Equal(MessageIds.WorkspaceCheckoutBundleTip, failure.Info.Id);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_tools_image_has_git_2_48_or_later_and_no_ssh_client_curl_or_interpreter()
    {
        Unless(engine.ToolsImage is not null, $"{EngineFixture.ToolsImageVariable} names no hephaestus-tools image.");
        var made = await PrepareAsync();

        var (exit, output) = await RunAsync(made, "git --version; command -v ssh curl python3 perl node || echo none", engine.ToolsImage);

        Assert.Equal(0, exit);
        var version = System.Version.Parse(output.Split('\n')[0].Split(' ')[2].Split('-')[0]);
        Assert.True(version >= new System.Version(2, 48), output);
        Assert.Equal("none", output.Split('\n')[^1]);
    }

    private static void Unless(bool condition, string reason)
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
