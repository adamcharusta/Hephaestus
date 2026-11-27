using Hephaestus.Core.Aliases;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Readiness;
using Hephaestus.Core.Secrets;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Readiness;

/// <summary>Story 3.10, FR-25, FR-26, AD-16, AD-20: how an Agent signs in is chosen from what its adapter supports, the credential becomes a secret, and a probe decides whether the Agent is ready.</summary>
public class AgentSignInServiceTests
{
    private const string Token = "sk-ant-oat01-the-credential-of-the-owner";

    /// <summary>A message is its id and its arguments, which a <see cref="Message"/> compares by reference, so they are compared here by value.</summary>
    private static void AssertMessage(Message expected, Message? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual.Value.Id);
        Assert.Equal(expected.Arguments.Select(argument => argument.ToString()), actual.Value.Arguments.Select(argument => argument.ToString()));
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeInstallation _installation = new();
    private readonly FakeAdapter _adapter = new();
    private readonly ProbeRuntime _runtime = new();
    private readonly RecordingSecrets _secrets;
    private readonly MovingTime _time = new();

    public AgentSignInServiceTests() => _secrets = new RecordingSecrets(_installation);

    private AgentSignInService Service(params IAgentAdapter[] adapters) =>
        new(adapters.Length == 0 ? [_adapter] : adapters, _installation, _installation, _secrets, _runtime, _time);

    [Fact]
    public async Task A_mode_the_adapter_supports_is_stored_and_the_credential_becomes_the_secret_its_reference_names()
    {
        var result = await Service().SetAuthenticationAsync("claude-code", "subscription", Token, Cancel);

        Assert.Equal(AgentSignInFailure.None, result.Failure);
        Assert.Equal(Token, _secrets.Values[FakeAdapter.SubscriptionSecret]);
        var setting = Assert.Single(_installation.AgentSettings.Values);
        Assert.Equal(new AgentSetting(Agent.ClaudeCode, AuthenticationMode.Subscription, _time.Now), setting);
        var status = result.Status!;
        Assert.Equal(AuthenticationMode.Subscription, status.Mode);
        Assert.Equal(FakeAdapter.SubscriptionSecret, status.Secret);
        Assert.True(status.CredentialStored);
        Assert.Null(status.Capacity);
    }

    [Fact]
    public async Task The_credential_is_stored_only_as_a_secret_and_is_never_read_back_or_part_of_the_answer()
    {
        var result = await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);

        Assert.Empty(_secrets.Reads);
        Assert.DoesNotContain(Token, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, string.Join('|', _installation.AgentSettings.Values.Select(s => s.ToString())), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mode_the_adapter_declares_unsupported_is_refused_with_the_adapters_reason_and_nothing_is_stored()
    {
        _adapter.Modes.Remove(AuthenticationMode.Subscription);

        var result = await Service().SetAuthenticationAsync("claude-code", "subscription", Token, Cancel);

        Assert.Equal(AgentSignInFailure.UnsupportedMode, result.Failure);
        AssertMessage(new Message(MessageIds.AgentAuthenticationUnsupported, "claude-code", "subscription"), result.Detail);
        Assert.Empty(_secrets.Values);
        Assert.Empty(_installation.AgentSettings);
    }

    [Theory]
    [InlineData("gemini", "api-key", AgentSignInFailure.UnknownAgent)]
    [InlineData(null, "api-key", AgentSignInFailure.UnknownAgent)]
    [InlineData("claude-code", "oauth", AgentSignInFailure.UnknownMode)]
    [InlineData("claude-code", null, AgentSignInFailure.UnknownMode)]
    [InlineData("codex-cli", "api-key", AgentSignInFailure.NoAdapter)]
    public async Task An_Agent_or_a_mode_that_is_not_known_and_an_Agent_with_no_adapter_are_refused_and_nothing_is_stored(string? agent, string? mode, AgentSignInFailure failure)
    {
        var result = await Service().SetAuthenticationAsync(agent, mode, Token, Cancel);

        Assert.Equal(failure, result.Failure);
        Assert.Empty(_secrets.Values);
        Assert.Empty(_installation.AgentSettings);
    }

    [Fact]
    public async Task An_Agent_with_no_adapter_is_refused_with_the_reason_that_this_release_has_none()
    {
        var result = await Service().SetAuthenticationAsync("codex-cli", "api-key", Token, Cancel);

        AssertMessage(new Message(MessageIds.AgentNoAdapter, "codex-cli"), result.Detail);
    }

    [Theory]
    [InlineData("short", SecretValueRefusal.TooShort)]
    [InlineData("a value with � in it", SecretValueRefusal.NotText)]
    public async Task A_credential_that_is_not_an_acceptable_secret_value_is_refused_and_nothing_is_stored(string credential, SecretValueRefusal refusal)
    {
        var result = await Service().SetAuthenticationAsync("claude-code", "api-key", credential, Cancel);

        Assert.Equal(AgentSignInFailure.CredentialRefused, result.Failure);
        Assert.Equal(refusal, result.Refusal);
        Assert.Empty(_secrets.Values);
        Assert.Empty(_installation.AgentSettings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task An_empty_credential_keeps_the_secret_that_is_stored(string? empty)
    {
        _installation.SecretNames.Add(FakeAdapter.ApiKeySecret);

        var result = await Service().SetAuthenticationAsync("claude-code", "api-key", empty, Cancel);

        Assert.Equal(AgentSignInFailure.None, result.Failure);
        Assert.Empty(_secrets.Values);
        Assert.Equal(AuthenticationMode.ApiKey, _installation.AgentSettings[Agent.ClaudeCode].Mode);
        Assert.True(result.Status!.CredentialStored);
    }

    [Fact]
    public async Task An_empty_credential_with_none_stored_is_refused_and_says_where_it_would_be_stored()
    {
        var result = await Service().SetAuthenticationAsync("claude-code", "api-key", string.Empty, Cancel);

        Assert.Equal(AgentSignInFailure.CredentialRequired, result.Failure);
        AssertMessage(new Message(MessageIds.AgentCredentialRequired, "claude-code", "api-key", FakeAdapter.ApiKeySecret), result.Detail);
        Assert.Empty(_installation.AgentSettings);
    }

    [Fact]
    public async Task Storing_a_credential_forgets_what_the_probe_found_of_its_mode_and_keeping_the_credential_does_not()
    {
        await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        await Service().CheckAsync("claude-code", Cancel);
        Assert.Single(_installation.AgentCapacities);

        await Service().SetAuthenticationAsync("claude-code", "api-key", string.Empty, Cancel);
        Assert.Single(_installation.AgentCapacities);

        await Service().SetAuthenticationAsync("claude-code", "api-key", Token + "-new", Cancel);
        Assert.Empty(_installation.AgentCapacities);
    }

    [Fact]
    public async Task A_probe_that_ends_well_finds_the_Agent_ready_and_records_it_with_the_time_of_the_probe()
    {
        await Service().SetAuthenticationAsync("claude-code", "subscription", Token, Cancel);
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await Service().CheckAsync("claude-code", Cancel);

        Assert.Equal(AgentSignInFailure.None, result.Failure);
        var capacity = result.Status!.Capacity!;
        Assert.Equal(AgentReadiness.Ready, capacity.Readiness);
        Assert.Null(capacity.Reason);
        Assert.Equal(_time.Now, capacity.ProbedAt);
        Assert.True(capacity.HasCapacity(_time.Now));
        Assert.Equal(["pong"], _adapter.Parsed);
        Assert.Equal(0, _adapter.ClassifiedExit);
    }

    [Fact]
    public async Task The_probe_the_runtime_is_asked_for_is_the_one_the_adapter_describes_for_the_chosen_mode_with_limits_of_its_own()
    {
        await Service().SetAuthenticationAsync("claude-code", "subscription", Token, Cancel);

        await Service().CheckAsync("claude-code", Cancel);

        var probe = Assert.Single(_runtime.Probes);
        Assert.Equal(_adapter.DescribeProbe(AuthenticationMode.Subscription).Command, probe.Invocation.Command);
        Assert.Equal(["api.fake.test"], probe.Invocation.EgressHosts);
        Assert.Equal([FakeAdapter.SubscriptionSecret], probe.Invocation.SecretReferences.Select(r => r.Name));
        Assert.Equal(AgentSignInService.ProbeLimits, probe.Limits);
        Assert.Equal(AgentSignInService.ProbeTimeLimit, probe.Limit);
        Assert.True(EntityIdIsVersion7(probe.ProbeId));
    }

    private static bool EntityIdIsVersion7(Guid id) => Core.Identity.EntityId.IsValid(id);

    [Fact]
    public async Task A_probe_the_adapter_reads_as_an_error_finds_the_Agent_unavailable_and_it_has_no_capacity()
    {
        await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        _adapter.Class = FailureClass.AgentError;
        _runtime.Run = _ => new ProbeResult(1, StepExitCause.Exited, ["not json"]);

        var result = await Service().CheckAsync("claude-code", Cancel);

        var capacity = result.Status!.Capacity!;
        Assert.Equal(AgentReadiness.Unavailable, capacity.Readiness);
        AssertMessage(new Message(MessageIds.AgentProbeError, "claude-code", "1"), capacity.Reason);
        Assert.False(capacity.HasCapacity(_time.Now));
        Assert.Equal(["not json"], _adapter.Parsed);
    }

    [Fact]
    public async Task A_probe_that_failed_without_a_cause_the_adapter_can_name_finds_the_Agent_unavailable_with_the_exit_code()
    {
        await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        _adapter.Class = FailureClass.Unknown;
        _runtime.Run = _ => new ProbeResult(137, StepExitCause.Exited, []);

        var capacity = (await Service().CheckAsync("claude-code", Cancel)).Status!.Capacity!;

        AssertMessage(new Message(MessageIds.AgentProbeUnknown, "claude-code", "137"), capacity.Reason);
    }

    [Fact]
    public async Task A_probe_the_runtime_stopped_at_its_limit_finds_the_Agent_unavailable_without_asking_the_adapter()
    {
        await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        _runtime.Run = _ => new ProbeResult(-1, StepExitCause.Timeout, []);

        var capacity = (await Service().CheckAsync("claude-code", Cancel)).Status!.Capacity!;

        Assert.Equal(AgentReadiness.Unavailable, capacity.Readiness);
        AssertMessage(new Message(MessageIds.AgentProbeTimedOut, "claude-code", "120"), capacity.Reason);
        Assert.Null(_adapter.ClassifiedExit);
    }

    [Theory]
    [InlineData(StepExitCause.OutOfMemory, "out-of-memory")]
    [InlineData(StepExitCause.Lost, "lost")]
    public async Task A_probe_that_ended_before_it_answered_finds_the_Agent_unavailable_with_what_the_runtime_saw(StepExitCause cause, string tag)
    {
        await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        _runtime.Run = _ => new ProbeResult(137, cause, []);

        var capacity = (await Service().CheckAsync("claude-code", Cancel)).Status!.Capacity!;

        AssertMessage(new Message(MessageIds.AgentProbeLost, "claude-code", tag), capacity.Reason);
        Assert.Null(_adapter.ClassifiedExit);
    }

    [Fact]
    public async Task A_credential_that_is_not_stored_is_a_finding_that_needs_no_probe()
    {
        await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        _installation.SecretNames.Clear();

        var result = await Service().CheckAsync("claude-code", Cancel);

        Assert.Empty(_runtime.Probes);
        var capacity = result.Status!.Capacity!;
        Assert.Equal(AgentReadiness.Unavailable, capacity.Readiness);
        AssertMessage(new Message(MessageIds.AgentProbeCredentialMissing, "claude-code", "api-key", FakeAdapter.ApiKeySecret), capacity.Reason);
        Assert.False(result.Status.CredentialStored);
    }

    [Fact]
    public async Task An_Agent_with_no_chosen_mode_is_refused_with_how_to_choose_one()
    {
        var result = await Service().CheckAsync("claude-code", Cancel);

        Assert.Equal(AgentSignInFailure.NotConfigured, result.Failure);
        AssertMessage(new Message(MessageIds.AgentNotConfigured, "claude-code", "api-key|subscription"), result.Detail);
        Assert.Empty(_runtime.Probes);
        Assert.Empty(_installation.AgentCapacities);
    }

    [Theory]
    [InlineData("gemini", AgentSignInFailure.UnknownAgent)]
    [InlineData("codex-cli", AgentSignInFailure.NoAdapter)]
    public async Task An_Agent_that_is_not_known_or_has_no_adapter_cannot_be_checked(string agent, AgentSignInFailure failure)
    {
        var result = await Service().CheckAsync(agent, Cancel);

        Assert.Equal(failure, result.Failure);
        Assert.Empty(_runtime.Probes);
    }

    [Fact]
    public async Task A_probe_that_cannot_run_is_not_a_finding_and_records_nothing()
    {
        await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        var reason = new Message(MessageIds.WorkspaceEgressProxyNotReady, "hephaestus");
        _runtime.Run = _ => throw new WorkspaceRuntimeException(reason);

        var result = await Service().CheckAsync("claude-code", Cancel);

        Assert.Equal(AgentSignInFailure.ProbeNotRun, result.Failure);
        AssertMessage(new Message(MessageIds.AgentProbeNotRun, "claude-code", reason), result.Detail);
        Assert.Empty(_installation.AgentCapacities);
    }

    [Fact]
    public async Task A_probe_image_that_cannot_be_used_is_not_a_finding_either()
    {
        await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        var reason = new Message(MessageIds.WorkspaceImageUnavailable, "debian", "no network");
        _runtime.Run = _ => throw new ImageNotUsableException(reason);

        var result = await Service().CheckAsync("claude-code", Cancel);

        Assert.Equal(AgentSignInFailure.ProbeNotRun, result.Failure);
        AssertMessage(new Message(MessageIds.AgentProbeNotRun, "claude-code", reason), result.Detail);
        Assert.Empty(_installation.AgentCapacities);
    }

    [Fact]
    public async Task A_stored_mode_that_the_adapter_no_longer_supports_is_the_Owners_to_change_and_runs_no_probe()
    {
        await Service().SetAuthenticationAsync("claude-code", "subscription", Token, Cancel);
        _adapter.Modes.Remove(AuthenticationMode.Subscription);

        var result = await Service().CheckAsync("claude-code", Cancel);

        Assert.Equal(AgentSignInFailure.UnsupportedMode, result.Failure);
        AssertMessage(new Message(MessageIds.AgentAuthenticationUnsupported, "claude-code", "subscription"), result.Detail);
        Assert.Empty(_runtime.Probes);
    }

    [Fact]
    public async Task What_a_probe_found_is_not_recorded_when_how_the_Agent_signs_in_changed_while_it_ran()
    {
        var service = Service();
        await service.SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        _runtime.Run = _ =>
        {
            _installation.AgentSettings[Agent.ClaudeCode] = new AgentSetting(Agent.ClaudeCode, AuthenticationMode.ApiKey, _time.Now.AddMinutes(1));
            return new ProbeResult(0, StepExitCause.Exited, ["pong"]);
        };

        var result = await service.CheckAsync("claude-code", Cancel);

        Assert.Equal(AgentSignInFailure.ProbeNotRun, result.Failure);
        AssertMessage(new Message(MessageIds.AgentProbeSuperseded, "claude-code"), result.Detail);
        Assert.Empty(_installation.AgentCapacities);
    }

    [Fact]
    public async Task Two_checks_do_not_probe_at_the_same_time()
    {
        var service = Service();
        await service.SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        await service.SetAuthenticationAsync("claude-code", "subscription", Token, Cancel);
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        _runtime.Hold = _ =>
        {
            started.TrySetResult();
            return release.Task;
        };

        var first = service.CheckAsync("claude-code", Cancel);
        await started.Task;
        var second = service.CheckAsync("claude-code", Cancel);
        await Task.Delay(50, Cancel);

        Assert.Single(_runtime.Probes);
        Assert.False(second.IsCompleted);

        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, _runtime.Probes.Count);
    }

    [Fact]
    public async Task A_check_that_is_cancelled_while_it_waits_for_another_does_not_probe()
    {
        var service = Service();
        await service.SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        _runtime.Hold = _ =>
        {
            started.TrySetResult();
            return release.Task;
        };
        var first = service.CheckAsync("claude-code", Cancel);
        await started.Task;
        using var cancel = new CancellationTokenSource();
        var second = service.CheckAsync("claude-code", cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        release.SetResult();
        await first;
        Assert.Single(_runtime.Probes);
    }

    [Fact]
    public void A_message_that_carries_another_message_is_resolved_in_one_language()
    {
        var reason = new Message(MessageIds.WorkspaceImageUnavailable, "debian", "no network");

        var english = MessageCatalog.Instance.Resolve(new Message(MessageIds.AgentProbeNotRun, "claude-code", reason), Language.English);
        var polish = MessageCatalog.Instance.Resolve(new Message(MessageIds.AgentProbeNotRun, "claude-code", reason), Language.Polish);

        Assert.Contains("could not run", english, StringComparison.Ordinal);
        Assert.Contains("the image debian could not be fetched: no network", english, StringComparison.Ordinal);
        Assert.Contains("nie mogła się uruchomić", polish, StringComparison.Ordinal);
        Assert.Contains("no network", polish, StringComparison.Ordinal);
        Assert.DoesNotContain("Message {", english, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_list_has_every_Agent_with_its_mode_credential_and_last_finding()
    {
        await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        await Service().CheckAsync("claude-code", Cancel);

        var agents = await Service().ListAsync(Cancel);

        Assert.Equal([Agent.ClaudeCode, Agent.CodexCli], agents.Select(a => a.Agent));
        var claude = agents[0];
        Assert.Equal(AuthenticationMode.ApiKey, claude.Mode);
        Assert.True(claude.CredentialStored);
        Assert.Equal(AgentReadiness.Ready, claude.Capacity!.Readiness);
        var codex = agents[1];
        Assert.Null(codex.Mode);
        Assert.Null(codex.Secret);
        Assert.Null(codex.CredentialStored);
        Assert.Null(codex.Capacity);
    }

    [Fact]
    public async Task The_finding_shown_is_the_one_of_the_mode_that_is_chosen_now()
    {
        await Service().SetAuthenticationAsync("claude-code", "api-key", Token, Cancel);
        await Service().CheckAsync("claude-code", Cancel);
        _installation.AgentSettings[Agent.ClaudeCode] = new AgentSetting(Agent.ClaudeCode, AuthenticationMode.Subscription, _time.Now);

        var claude = (await Service().ListAsync(Cancel))[0];

        Assert.Equal(AuthenticationMode.Subscription, claude.Mode);
        Assert.Null(claude.Capacity);
        Assert.False(claude.CredentialStored);
    }

    [Fact]
    public void An_Agent_that_is_ready_has_no_capacity_while_a_usage_limit_holds_it()
    {
        var until = _time.Now.AddHours(1);
        var held = new AgentCapacity(Agent.ClaudeCode, AuthenticationMode.ApiKey, AgentReadiness.Ready, null, [], _time.Now, until);

        Assert.False(held.HasCapacity(_time.Now));
        Assert.True(held.HasCapacity(until));
    }

    [Fact]
    public void Two_adapters_of_one_Agent_are_a_mistake_of_whoever_composes_the_host()
    {
        Assert.Throws<ArgumentException>(() => Service(new FakeAdapter(), new FakeAdapter()));
    }

    [Fact]
    public void A_readiness_has_a_name_that_is_read_back()
    {
        foreach (var readiness in Enum.GetValues<AgentReadiness>())
        {
            Assert.True(AgentReadinesses.TryParse(readiness.Tag(), out var parsed));
            Assert.Equal(readiness, parsed);
        }

        Assert.False(AgentReadinesses.TryParse("maybe", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((AgentReadiness)99).Tag());
    }

    [Fact]
    public void The_probe_image_is_pinned_by_digest()
    {
        Assert.Matches("^docker\\.io/library/debian:[0-9a-z.-]+@sha256:[0-9a-f]{64}$", ProbeImage.Reference);
    }
}
