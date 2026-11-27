using System.Formats.Tar;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>Story 3.2, AD-6: a Task owns one named volume, mounted without copying from the image, whose root the step user can write to.</summary>
public class TaskVolumeTests
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private const string Key = "task:g4:volume:0";

    private readonly DockerDouble _docker = new();

    private static readonly ResolvedImage Image = new("registry.test/app@sha256:" + new string('b', 64), "sha256:" + new string('b', 64), AgentArchitecture.X64, CLibrary.Musl);

    private Task EnsureAsync() =>
        Runtimes.Of(_docker.Client, new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, AgentArchitecture.X64), new RecordingWatcher())
            .EnsureTaskVolumeAsync(TaskId, Image, Key, TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_volume_is_named_after_the_task_and_carries_the_labels_of_the_convention()
    {
        await EnsureAsync();

        var volume = Assert.Single(_docker.Volumes);
        Assert.Equal($"hephaestus-ws-{TaskId}", volume.Name);
        Assert.Equal("test-installation", volume.Labels!["hephaestus.installation"]);
        Assert.Equal(TaskId.ToString(), volume.Labels!["hephaestus.task"]);
        Assert.Equal(string.Empty, volume.Labels!["hephaestus.step-run"]);
        Assert.Equal(Key, volume.Labels!["hephaestus.effect"]);
    }

    [Fact]
    public async Task The_volume_is_a_named_volume_and_no_host_path_is_mounted_anywhere()
    {
        await EnsureAsync();

        var helper = Assert.Single(_docker.Created);
        var mount = Assert.Single(helper.HostConfig!.Mounts!);
        Assert.Equal("volume", mount.Type);
        Assert.Equal($"hephaestus-ws-{TaskId}", mount.Source);
        Assert.Equal("/workspace", mount.Target);
        Assert.Empty(helper.HostConfig!.Binds! ?? []);
    }

    [Fact]
    public async Task The_helper_mounts_the_volume_without_copying_from_the_image()
    {
        await EnsureAsync();

        Assert.True(Assert.Single(_docker.Created).HostConfig!.Mounts![0].VolumeOptions!.NoCopy);
    }

    [Fact]
    public async Task The_root_of_the_volume_is_handed_to_the_step_user_through_a_container_that_never_runs()
    {
        await EnsureAsync();

        var helper = Assert.Single(_docker.Created);
        Assert.Equal("none", helper.HostConfig!.NetworkMode);
        Assert.Equal("registry.test/app@sha256:" + new string('b', 64), helper.Image);
        var extraction = Assert.Single(_docker.Extractions);
        Assert.Equal("/", extraction.Parameters.Path);
        Assert.True(extraction.Parameters.CopyUIDGID);
        using var reader = new TarReader(new MemoryStream(extraction.Tar));
        var entry = reader.GetNextEntry()!;
        Assert.Equal(TarEntryType.Directory, entry.EntryType);
        Assert.Equal("workspace", entry.Name.TrimEnd('/'));
        Assert.Equal(10001, entry.Uid);
        Assert.Equal(10001, entry.Gid);
        Assert.True(entry.Mode.HasFlag(UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.UserRead));
        Assert.Empty(_docker.Started);
        Assert.Contains($"id-{helper.Name}", _docker.Removed);
    }

    [Fact]
    public async Task A_second_call_for_the_same_task_leaves_the_volume_and_sets_the_owner_again()
    {
        await EnsureAsync();
        await EnsureAsync();

        Assert.Equal(2, _docker.Extractions.Count);
        Assert.Equal(2, _docker.Volumes.Count);
        Assert.Equal(new HashSet<string> { $"hephaestus-ws-{TaskId}" }, _docker.Volumes.Select(volume => volume.Name).ToHashSet());
        Assert.Contains($"remove hephaestus-volume-helper-{TaskId}", _docker.Calls);
    }

    [Fact]
    public async Task A_failure_to_set_the_owner_removes_the_helper_and_is_reported()
    {
        _docker.ExtractFails = new IOException("no space left");

        await Assert.ThrowsAsync<IOException>(EnsureAsync);

        Assert.Contains("id-hephaestus-volume-helper-" + TaskId, _docker.Removed);
    }
}
