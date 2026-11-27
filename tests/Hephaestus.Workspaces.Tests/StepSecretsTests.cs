using System.Text.Json;
using Docker.DotNet.Models;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Invocations;
using SecretReference = Hephaestus.Core.Invocations.SecretReference;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>Story 3.10, FR-18, AD-16: a step container resolves the secret references of its own invocation, and only those, through the secret store at the moment it starts, and the value goes nowhere but its environment.</summary>
public class StepSecretsTests
{
    private const string ApiKey = "sk-ant-api03-the-key-of-the-owner";
    private const string OtherKey = "sk-ant-oat01-another-agents-token";
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
    private static readonly SecretReference Reference = new("anthropic-api-key", "ANTHROPIC_API_KEY");

    private readonly DockerDouble _docker = new();
    private readonly FakeVendor _vendor = new();
    private readonly TestSecrets _secrets = new TestSecrets().With("anthropic-api-key", ApiKey).With("openai-api-key", OtherKey);

    private DockerWorkspaceRuntime Runtime() =>
        Runtimes.Of(_docker.Client, new WorkspaceRuntimeOptions("test-installation", _vendor.Lock, AgentArchitecture.X64), new RecordingWatcher(), _secrets);

    private static StepContainerRequest Request(IReadOnlyList<SecretReference>? references, IReadOnlyDictionary<string, string>? environment = null) => new(
        TaskId,
        StepRunId,
        new ResolvedImage("registry.test/app@sha256:" + new string('a', 64), "sha256:" + new string('a', 64), AgentArchitecture.X64, CLibrary.Glibc),
        ["/hephaestus/agents/claude-code/claude", "-p", "Read /hephaestus/context/instruction.md"],
        environment ?? new Dictionary<string, string> { ["DISABLE_AUTOUPDATER"] = "1" },
        new StepLimits(2000, 3L * 1024 * 1024 * 1024),
        [new WorkspaceFile("instruction.md", [1])],
        [new WorkspaceFile("manifest.yml", [2])],
        new DateTimeOffset(2026, 11, 27, 13, 0, 0, TimeSpan.Zero),
        ["api.anthropic.com"],
        references);

    private async Task<CreateContainerParameters> StartedAsync(IReadOnlyList<SecretReference>? references)
    {
        await Runtime().StartStepAsync(Request(references), "task:s1:container:0", TestContext.Current.CancellationToken);
        return _docker.Created.Single(created => created.Name == $"hephaestus-step-{StepRunId}");
    }

    [Fact]
    public async Task The_reference_of_the_invocation_is_resolved_when_the_container_starts_and_its_value_is_in_the_environment()
    {
        await _secrets.ReadyAsync();

        var container = await StartedAsync([Reference]);

        Assert.Contains($"ANTHROPIC_API_KEY={ApiKey}", container.Env!);
        Assert.Contains("DISABLE_AUTOUPDATER=1", container.Env!);
        Assert.Equal(["anthropic-api-key"], _secrets.Reads);
    }

    [Fact]
    public async Task Only_the_secrets_the_invocation_references_are_read_and_no_value_is_read_without_its_name()
    {
        await _secrets.ReadyAsync();

        var container = await StartedAsync([Reference]);

        Assert.DoesNotContain("openai-api-key", _secrets.Reads);
        Assert.Equal(0, _secrets.ReadAlls);
        Assert.DoesNotContain(container.Env!, entry => entry.Contains(OtherKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_value_is_in_no_label_no_name_no_mount_and_no_other_container_of_the_step_run()
    {
        await _secrets.ReadyAsync();

        await StartedAsync([Reference]);

        foreach (var created in _docker.Created)
        {
            var visible = JsonSerializer.Serialize(new { created.Name, created.Labels, created.Image, created.HostConfig?.Mounts, created.HostConfig?.NetworkMode, created.Cmd, created.Entrypoint, created.WorkingDir });
            Assert.DoesNotContain(ApiKey, visible, StringComparison.Ordinal);
            if (created.Name != $"hephaestus-step-{StepRunId}")
            {
                Assert.DoesNotContain(created.Env ?? [], entry => entry.Contains(ApiKey, StringComparison.Ordinal));
            }
        }

        Assert.All(_docker.Volumes, volume => Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(volume), StringComparison.Ordinal));
        Assert.All(_docker.Networks, network => Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(network), StringComparison.Ordinal));
        Assert.All(_docker.Calls, call => Assert.DoesNotContain(ApiKey, call, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_container_that_runs_no_invocation_reads_no_secret()
    {
        await StartedAsync(null);
        _docker.Created.Clear();
        _docker.Existing.Clear();
        await StartedAsync([]);

        Assert.Empty(_secrets.Reads);
        Assert.Equal(0, _secrets.ReadAlls);
    }

    [Fact]
    public async Task A_secret_that_is_not_stored_refuses_the_start_before_anything_is_made()
    {
        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().StartStepAsync(Request([new SecretReference("claude-code-oauth-token", "CLAUDE_CODE_OAUTH_TOKEN")]), "k", TestContext.Current.CancellationToken));

        Assert.Equal(MessageIds.WorkspaceSecretMissing, refusal.Info.Id);
        Assert.Equal(["claude-code-oauth-token"], refusal.Info.Arguments);
        Assert.Empty(_docker.Created);
        Assert.Empty(_docker.Networks);
        Assert.Empty(_docker.Volumes);
    }

    [Fact]
    public async Task A_container_that_exists_already_is_found_and_its_secret_is_not_read_again()
    {
        _docker.Existing.Add(new ContainerListResponse { ID = "left", Names = [$"/hephaestus-step-{StepRunId}"], State = "running" });

        var started = await Runtime().StartStepAsync(Request([Reference]), "k", TestContext.Current.CancellationToken);

        Assert.False(started.Created);
        Assert.Empty(_secrets.Reads);
        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task The_secret_replaces_a_variable_of_the_same_name_that_the_invocation_set()
    {
        var container = await StartedAndWithAsync([Reference], new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "from-the-invocation", ["OTHER"] = "kept" });

        Assert.Single(container.Env!, entry => entry.StartsWith("ANTHROPIC_API_KEY=", StringComparison.Ordinal));
        Assert.Contains($"ANTHROPIC_API_KEY={ApiKey}", container.Env!);
        Assert.Contains("OTHER=kept", container.Env!);
    }

    private async Task<CreateContainerParameters> StartedAndWithAsync(IReadOnlyList<SecretReference> references, IReadOnlyDictionary<string, string> environment)
    {
        await Runtime().StartStepAsync(Request(references, environment), "k", TestContext.Current.CancellationToken);
        return _docker.Created.Single(created => created.Name == $"hephaestus-step-{StepRunId}");
    }

    [Theory]
    [InlineData("/run/secrets/key")]
    [InlineData("HOME")]
    [InlineData("HTTPS_PROXY")]
    public async Task A_target_that_is_a_file_or_a_variable_the_runtime_sets_itself_is_refused_before_anything_is_made(string target)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartStepAsync(Request([new SecretReference("anthropic-api-key", target)]), "k", TestContext.Current.CancellationToken));

        Assert.Empty(_docker.Created);
        Assert.Empty(_secrets.Reads);
    }

    [Fact]
    public async Task Two_references_with_one_target_are_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartStepAsync(Request([Reference, new SecretReference("openai-api-key", "ANTHROPIC_API_KEY")]), "k", TestContext.Current.CancellationToken));

        Assert.Empty(_docker.Created);
    }
}
