using Docker.DotNet;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;
using NSubstitute;

namespace Hephaestus.Workspaces.Tests;

/// <summary>Story 3.2, AD-8: the runtime resolves a Project's image to a digest and a C library when a Task starts, and refuses an image it cannot establish.</summary>
public class ImageResolutionTests
{
    private static readonly string Digest = "sha256:" + new string('c', 64);

    private readonly DockerDouble _docker = new();

    private Task<ResolvedImage> ResolveAsync(string image, AgentArchitecture architecture = AgentArchitecture.X64) =>
        new DockerWorkspaceRuntime(_docker.Client, new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, architecture), new RecordingWatcher())
            .ResolveImageAsync(image, TestContext.Current.CancellationToken);

    private static Task<ImageNotUsableException> RefusalAsync(Task<ResolvedImage> task) => Assert.ThrowsAsync<ImageNotUsableException>(() => task);

    [Fact]
    public async Task A_glibc_image_is_established_from_its_loader_without_running_anything()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        var resolved = await ResolveAsync("registry.test/app:1.0");

        Assert.Equal(CLibrary.Glibc, resolved.Libc);
        Assert.Equal(AgentArchitecture.X64, resolved.Architecture);
        Assert.DoesNotContain(_docker.Calls, call => call.StartsWith("start ", StringComparison.Ordinal));
        Assert.Empty(_docker.Started);
    }

    [Fact]
    public async Task A_musl_image_is_established_from_its_loader()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib/ld-musl-x86_64.so.1");

        Assert.Equal(CLibrary.Musl, (await ResolveAsync("registry.test/app:1.0")).Libc);
    }

    [Fact]
    public async Task An_image_with_both_loaders_is_musl_because_a_musl_build_runs_where_glibc_is_only_emulated()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.UnionWith(["/lib/ld-musl-x86_64.so.1", "/lib64/ld-linux-x86-64.so.2"]);

        Assert.Equal(CLibrary.Musl, (await ResolveAsync("registry.test/app:1.0")).Libc);
    }

    [Fact]
    public async Task On_arm64_the_loaders_of_arm64_are_the_ones_looked_for()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "arm64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib/ld-linux-aarch64.so.1");

        var resolved = await ResolveAsync("registry.test/app:1.0", AgentArchitecture.Arm64);

        Assert.Equal(CLibrary.Glibc, resolved.Libc);
        Assert.Equal(AgentArchitecture.Arm64, resolved.Architecture);
        Assert.Equal(["/lib/ld-musl-aarch64.so.1", "/lib/ld-linux-aarch64.so.1"], _docker.Statted);
    }

    [Fact]
    public async Task The_digest_and_the_reference_that_every_container_is_created_from_are_the_registrys()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", "other.test/mirror@sha256:" + new string('d', 64), $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        var resolved = await ResolveAsync("registry.test/app:1.0");

        Assert.Equal($"registry.test/app@{Digest}", resolved.Reference);
        Assert.Equal(Digest, resolved.Digest);
    }

    [Fact]
    public async Task A_registry_with_a_port_is_not_mistaken_for_a_tag()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", "other.test/x@sha256:" + new string('d', 64), $"localhost:5000/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        Assert.Equal($"localhost:5000/app@{Digest}", (await ResolveAsync("localhost:5000/app")).Reference);
    }

    [Fact]
    public async Task A_name_that_carries_a_digest_is_matched_to_that_repository()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", "other.test/x@sha256:" + new string('d', 64), $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        Assert.Equal($"registry.test/app@{Digest}", (await ResolveAsync($"registry.test/app@{Digest}")).Reference);
    }

    [Fact]
    public async Task An_image_that_only_has_another_repositorys_digest_is_pinned_by_that_digest()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"other.test/mirror@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        var resolved = await ResolveAsync("registry.test/app:1.0");

        Assert.Equal($"other.test/mirror@{Digest}", resolved.Reference);
        Assert.Equal(Digest, resolved.Digest);
    }

    [Fact]
    public async Task An_image_built_on_this_machine_is_pinned_by_its_id()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:" + new string('e', 64), "amd64");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");
        _docker.PullFails = new DockerApiException(System.Net.HttpStatusCode.NotFound, "pull access denied");

        var resolved = await ResolveAsync("local/app:dev");

        Assert.Equal("sha256:" + new string('e', 64), resolved.Reference);
        Assert.Equal("sha256:" + new string('e', 64), resolved.Digest);
    }

    [Fact]
    public async Task An_image_that_is_not_on_the_machine_is_fetched_for_the_platform_of_the_machine()
    {
        _docker.PulledImage = DockerDouble.ImageOf("sha256:id", "arm64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib/ld-musl-aarch64.so.1");

        var resolved = await ResolveAsync("registry.test/app:1.0", AgentArchitecture.Arm64);

        var pull = Assert.Single(_docker.Pulls);
        Assert.Equal("registry.test/app:1.0", pull.FromImage);
        Assert.Equal("linux/arm64", pull.Platform);
        Assert.Equal(CLibrary.Musl, resolved.Libc);
    }

    [Fact]
    public async Task A_tag_is_fetched_again_so_that_a_tag_that_moved_is_the_one_pinned()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        await ResolveAsync("registry.test/app:latest");

        Assert.Equal("linux/amd64", Assert.Single(_docker.Pulls).Platform);
    }

    [Fact]
    public async Task A_name_that_carries_a_digest_and_is_on_the_machine_is_not_fetched()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        await ResolveAsync($"registry.test/app@{Digest}");

        Assert.Empty(_docker.Pulls);
    }

    [Fact]
    public async Task An_image_that_cannot_be_fetched_and_is_not_on_the_machine_stops_the_task_and_says_why()
    {
        _docker.PullFails = new DockerApiException(System.Net.HttpStatusCode.InternalServerError, "unauthorized");

        var refusal = await RefusalAsync(ResolveAsync("registry.test/private:1"));

        Assert.Equal(MessageIds.WorkspaceImageUnavailable, refusal.Info.Id);
        Assert.Equal("registry.test/private:1", refusal.Info.Arguments[0]);
        Assert.Contains("unauthorized", (string)refusal.Info.Arguments[1], StringComparison.Ordinal);
        Assert.IsType<DockerApiException>(refusal.InnerException);
    }

    [Fact]
    public async Task A_fetch_that_leaves_nothing_on_the_machine_stops_the_task()
    {
        var refusal = await RefusalAsync(ResolveAsync("registry.test/app:1.0"));

        Assert.Equal(MessageIds.WorkspaceImageMissing, refusal.Info.Id);
        Assert.Equal(["registry.test/app:1.0"], refusal.Info.Arguments);
    }

    [Fact]
    public async Task An_unreachable_registry_is_an_image_that_cannot_be_fetched()
    {
        _docker.PullFails = new HttpRequestException("connection refused");

        Assert.Equal(MessageIds.WorkspaceImageUnavailable, (await RefusalAsync(ResolveAsync("registry.test/app:1.0"))).Info.Id);
    }

    [Fact]
    public async Task An_image_built_for_another_architecture_stops_the_task_and_says_which()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "arm64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        var refusal = await RefusalAsync(ResolveAsync("registry.test/app:1.0"));

        Assert.Equal(MessageIds.WorkspaceImageArchitecture, refusal.Info.Id);
        Assert.Equal(["registry.test/app:1.0", "arm64", "amd64"], refusal.Info.Arguments);
        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task An_image_whose_C_library_cannot_be_established_stops_the_task_and_says_so()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");

        var refusal = await RefusalAsync(ResolveAsync("registry.test/distroless:static"));

        Assert.Equal(MessageIds.WorkspaceImageLibc, refusal.Info.Id);
        Assert.Equal(["registry.test/distroless:static"], refusal.Info.Arguments);
    }

    [Fact]
    public async Task The_probe_is_inert_labelled_and_removed_even_when_the_library_cannot_be_established()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");

        await RefusalAsync(ResolveAsync("registry.test/distroless:static"));

        var probe = Assert.Single(_docker.Created);
        Assert.Equal("none", probe.HostConfig!.NetworkMode);
        Assert.Equal("image-probe", probe.Labels!["hephaestus.role"]);
        Assert.Equal("test-installation", probe.Labels!["hephaestus.installation"]);
        Assert.StartsWith("hephaestus-", probe.Name, StringComparison.Ordinal);
        Assert.Contains($"id-{probe.Name}", _docker.Removed);
        Assert.Empty(_docker.Started);
    }

    [Fact]
    public async Task The_probe_is_created_from_the_pinned_reference()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        await ResolveAsync("registry.test/app:1.0");

        Assert.Equal($"registry.test/app@{Digest}", Assert.Single(_docker.Created).Image);
    }

    [Fact]
    public async Task An_image_that_declares_volumes_stops_the_task_and_names_them()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");
        _docker.Image.Config = new Docker.DotNet.Models.DockerOCIImageConfig { Volumes = new Dictionary<string, Docker.DotNet.Models.EmptyStruct> { ["/var/lib/data"] = new(), ["/cache"] = new() } };
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        var refusal = await RefusalAsync(ResolveAsync("registry.test/app:1.0"));

        Assert.Equal(MessageIds.WorkspaceImageVolumes, refusal.Info.Id);
        Assert.Equal(["registry.test/app:1.0", "/cache, /var/lib/data"], refusal.Info.Arguments);
        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task A_pull_that_fails_in_its_stream_is_an_image_that_cannot_be_fetched()
    {
        _docker.PullMessages.Add(new Docker.DotNet.Models.JSONMessage { Error = new Docker.DotNet.Models.JSONError { Message = "manifest unknown" } });

        var refusal = await RefusalAsync(ResolveAsync("registry.test/app:9"));

        Assert.Equal(MessageIds.WorkspaceImageUnavailable, refusal.Info.Id);
        Assert.Equal("manifest unknown", refusal.Info.Arguments[1]);
    }

    [Fact]
    public async Task A_pull_that_fails_in_its_stream_leaves_the_copy_on_the_machine_in_use()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");
        _docker.PullMessages.Add(new Docker.DotNet.Models.JSONMessage { Error = new Docker.DotNet.Models.JSONError { Message = "denied" } });

        Assert.Equal($"registry.test/app@{Digest}", (await ResolveAsync("registry.test/app:1.0")).Reference);
    }

    [Fact]
    public async Task A_pull_that_outlasts_the_clients_limit_is_an_image_that_cannot_be_fetched()
    {
        _docker.PullFails = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        Assert.Equal(MessageIds.WorkspaceImageUnavailable, (await RefusalAsync(ResolveAsync("registry.test/app:1.0"))).Info.Id);
    }

    [Fact]
    public async Task A_name_without_a_tag_is_pulled_as_latest_so_that_docker_does_not_pull_every_tag()
    {
        _docker.PulledImage = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        await ResolveAsync("registry.test/app");
        _docker.Image = null;
        await ResolveAsync("localhost:5000/app");

        Assert.Equal(["registry.test/app:latest", "localhost:5000/app:latest"], _docker.Pulls.Select(pull => pull.FromImage));
    }

    [Fact]
    public async Task A_copy_with_the_digest_but_for_another_architecture_does_not_stop_the_fetch_of_the_right_one()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "arm64", $"registry.test/app@{Digest}");
        _docker.PulledImage = DockerDouble.ImageOf("sha256:id2", "amd64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");
        _docker.Client.Images.CreateImageAsync(Arg.Any<Docker.DotNet.Models.ImagesCreateParameters>(), Arg.Any<Docker.DotNet.Models.AuthConfig>(), Arg.Any<IProgress<Docker.DotNet.Models.JSONMessage>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _docker.Image = _docker.PulledImage;
            return Task.CompletedTask;
        });

        var resolved = await ResolveAsync($"registry.test/app@{Digest}");

        Assert.Equal(AgentArchitecture.X64, resolved.Architecture);
    }

    [Fact]
    public async Task The_probe_of_an_image_with_no_entry_point_is_created_with_one_so_that_docker_does_not_refuse_it()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:id", "amd64", $"registry.test/app@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");

        await ResolveAsync("registry.test/app:1.0");

        Assert.Equal(["/hephaestus-never-started"], Assert.Single(_docker.Created).Entrypoint);
    }
}
