using System.Runtime.Versioning;
using System.Security.Cryptography;
using Docker.DotNet.Models;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.6 against a real Docker Engine and the real tools image (AD-9, FR-20, AR-50): a step changes the workspace and plants a hostile <c>.git</c> in it, and the checkpoint comes out as one commit on the last pushed tip whose
/// bundle the runtime read from the stopped container, without using that <c>.git</c> and without writing to the volume. The tests are skipped where no Engine answers and run in CI, where the tools image built for the run is named by
/// <see cref="EngineFixture.ToolsImageVariable"/>.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class CheckpointEngineTests(EngineFixture engine) : IClassFixture<EngineFixture>
{
    private const string Branch = EngineWorkspaces.Branch;
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly EngineWorkspaces _workspaces = new(engine);

    private static CheckpointCommit CommitOf(Guid taskId, Guid stepRunId) =>
        CheckpointCommit.Compose(new CommitIdentity("hephaestus-test[bot]", "hephaestus-test[bot]@users.noreply.github.com"), Language.English, StepKind.Implementation, taskId, stepRunId, Agent.ClaudeCode, "opus5.5", Effort.High);

    /// <summary>The bundle of the tip of the repository the checkout was made from, as the gateway makes it for the checkpoint.</summary>
    private async Task<BundleReference> ParentBundleAsync(EngineWorkspaces.Made made, Guid step)
    {
        var name = $"{made.TaskId:D}/{step:D}-checkout.bundle";
        var path = Path.Combine(engine.BundlesDirectory, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        LocalGit.Bundle(made.Repository, path, "main");
        await using var file = File.OpenRead(path);
        return new BundleReference(name, Convert.ToHexStringLower(await SHA256.HashDataAsync(file, Cancel)), made.Tip);
    }

    private async Task<CheckpointResult> CheckpointAsync(EngineWorkspaces.Made made, Guid step) =>
        await engine.Runtime.CheckpointAsync(new CheckpointRequest(made.TaskId, step, StepKind.Implementation, Branch, await ParentBundleAsync(made, step), CommitOf(made.TaskId, step)), $"task:c1:checkpoint:{step:D}", Cancel);

    private const string Agent_ = "cd /workspace && echo edited >> README.md && echo added > added.txt && rm src/app.txt && mkdir -p node_modules && echo x > node_modules/ignored";

    private const string Hostile = "cd /workspace && mkdir -p .git/hooks && printf '#!/bin/sh\\nexit 1\\n' > .git/hooks/reference-transaction && chmod +x .git/hooks/reference-transaction"
        + " && printf '[core]\\n\\trepositoryformatversion = 1\\n\\tfsmonitor = false\\n[extensions]\\n\\tbogus = true\\n' >> .git/config";

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_changed_workspace_is_one_commit_on_the_last_pushed_tip_in_a_bundle_the_runtime_read_from_the_container()
    {
        var made = await _workspaces.PrepareAsync();
        await _workspaces.CheckoutAsync(made);
        Assert.Equal(0, (await _workspaces.RunAsync(made, Agent_ + " && git -c user.name=a -c user.email=a@a.a commit -qam 'agent commit' ", engine.ToolsImage)).ExitCode);
        var step = Guid.CreateVersion7();

        var result = await CheckpointAsync(made, step);

        var bundle = result.Bundle!;
        Assert.Equal(made.Tip, bundle.Prerequisite);
        var file = Path.Combine(engine.BundlesDirectory, bundle.Path.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(bundle.Sha256, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file, Cancel))));
        LocalGit.Git(made.Repository, "fetch", "--quiet", file, $"refs/heads/{Branch}:refs/heads/checkpoint");
        Assert.Equal(bundle.Tip, LocalGit.Git(made.Repository, "rev-parse", "checkpoint"));
        Assert.Equal(made.Tip, LocalGit.Git(made.Repository, "log", "-1", "--format=%P", "checkpoint"));
        Assert.Equal("1", LocalGit.Git(made.Repository, "rev-list", "--count", $"{made.Tip}..checkpoint"));
        Assert.Equal([".gitignore", "README.md", "added.txt"], LocalGit.Git(made.Repository, "ls-tree", "-r", "--name-only", "checkpoint").Split('\n').Order(StringComparer.Ordinal));
        Assert.Equal("hephaestus-test[bot]", LocalGit.Git(made.Repository, "log", "-1", "--format=%cn", "checkpoint"));
        Assert.Equal(CommitOf(made.TaskId, step).Message, LocalGit.Git(made.Repository, "log", "-1", "--format=%B", "checkpoint") + "\n");
        File.Delete(file);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_git_directory_with_hostile_configuration_and_hooks_is_never_used_and_the_volume_is_not_written_to()
    {
        var made = await _workspaces.PrepareAsync();
        await _workspaces.CheckoutAsync(made);
        Assert.Equal(0, (await _workspaces.RunAsync(made, Agent_ + " && " + Hostile.Replace("cd /workspace && ", string.Empty, StringComparison.Ordinal), engine.ToolsImage)).ExitCode);
        const string Fingerprint = "cd /workspace && find . -type f -exec sha256sum {} + | sort | sha256sum && find . | wc -l";
        var before = await _workspaces.RunAsync(made, Fingerprint, engine.ToolsImage);
        var step = Guid.CreateVersion7();

        var result = await CheckpointAsync(made, step);

        Assert.False(result.IsUnchanged);
        Assert.Equal(before, await _workspaces.RunAsync(made, Fingerprint, engine.ToolsImage));
        File.Delete(Path.Combine(engine.BundlesDirectory, result.Bundle!.Path.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_repository_the_agent_made_inside_the_workspace_is_not_committed_as_a_link_to_a_commit_with_the_busybox_of_the_tools_image()
    {
        var made = await _workspaces.PrepareAsync();
        await _workspaces.CheckoutAsync(made);
        const string Nested = "cd /workspace && mkdir -p vendor/lib && cd vendor/lib && git init -q && git -c user.name=a -c user.email=a@a.a commit -q --allow-empty -m nested && cd /workspace && echo added > added.txt && chmod 000 added.txt && echo kept > kept.txt";
        Assert.Equal(0, (await _workspaces.RunAsync(made, Nested, engine.ToolsImage)).ExitCode);
        var step = Guid.CreateVersion7();

        var result = await CheckpointAsync(made, step);

        var file = Path.Combine(engine.BundlesDirectory, result.Bundle!.Path.Replace('/', Path.DirectorySeparatorChar));
        LocalGit.Git(made.Repository, "fetch", "--quiet", file, $"refs/heads/{Branch}:refs/heads/checkpoint");
        Assert.DoesNotContain("160000", LocalGit.Git(made.Repository, "ls-tree", "-r", "checkpoint"));
        Assert.Contains("kept.txt", LocalGit.Git(made.Repository, "ls-tree", "-r", "--name-only", "checkpoint"));
        File.Delete(file);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task An_unchanged_workspace_makes_no_bundle_and_the_containers_are_gone()
    {
        var made = await _workspaces.PrepareAsync();
        await _workspaces.CheckoutAsync(made);
        var step = Guid.CreateVersion7();

        var result = await CheckpointAsync(made, step);

        Assert.True(result.IsUnchanged);
        Assert.False(File.Exists(Path.Combine(engine.BundlesDirectory, made.TaskId.ToString(), $"{step}-checkpoint.bundle")));
        Assert.Empty(await engine.Docker.Containers.ListContainersAsync(new ContainersListParameters { All = true, Filters = new Dictionary<string, IDictionary<string, bool>> { ["name"] = new Dictionary<string, bool> { [$"hephaestus-checkpoint-{step}"] = true } } }, Cancel));
        Assert.False(File.Exists(Path.Combine(engine.BundlesDirectory, made.TaskId.ToString(), $"{step}-checkout.bundle")));
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_next_checkout_after_a_checkpoint_gives_the_step_a_clean_workspace_again()
    {
        var made = await _workspaces.PrepareAsync();
        await _workspaces.CheckoutAsync(made);
        Assert.Equal(0, (await _workspaces.RunAsync(made, Agent_, engine.ToolsImage)).ExitCode);
        var result = await CheckpointAsync(made, Guid.CreateVersion7());
        File.Delete(Path.Combine(engine.BundlesDirectory, result.Bundle!.Path.Replace('/', Path.DirectorySeparatorChar)));

        await _workspaces.CheckoutAsync(made);

        var (exit, output) = await _workspaces.RunAsync(made, "cd /workspace && git status --porcelain --ignored && ls", engine.ToolsImage);
        Assert.Equal(0, exit);
        Assert.DoesNotContain("added.txt", output, StringComparison.Ordinal);
        Assert.DoesNotContain("node_modules", output, StringComparison.Ordinal);
    }
}
