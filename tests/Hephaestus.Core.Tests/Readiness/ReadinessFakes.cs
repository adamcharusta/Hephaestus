using Hephaestus.Core.Aliases;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Readiness;

/// <summary>An adapter that only describes a probe: which modes it supports, which secret each needs, and how an exit is read, are the test's.</summary>
internal sealed class FakeAdapter(Agent agent = Agent.ClaudeCode) : IAgentAdapter
{
    public const string ApiKeySecret = "fake-api-key";
    public const string SubscriptionSecret = "fake-subscription-token";

    public Agent Agent { get; } = agent;

    public string PinnedVersion => "1.0.0";

    /// <summary>The modes the adapter supports.</summary>
    public HashSet<AuthenticationMode> Modes { get; } = [AuthenticationMode.ApiKey, AuthenticationMode.Subscription];

    /// <summary>What <see cref="ClassifyExit"/> answers; null reads the exit as ended well.</summary>
    public FailureClass? Class { get; set; }

    /// <summary>The lines and exit code the last <see cref="ClassifyExit"/> was given.</summary>
    public int? ClassifiedExit { get; private set; }

    public List<string> Parsed { get; } = [];

    public Support SupportsAuthentication(AuthenticationMode mode) =>
        Modes.Contains(mode) ? Support.Yes : Support.No(new Message(MessageIds.AgentAuthenticationUnsupported, Agent.Tag(), mode.Tag()));

    public Support SupportsEffort(Effort effort) => Support.Yes;

    public AgentInvocation DescribeInvocation(InvocationRequest request) => throw new NotSupportedException();

    public AgentInvocation DescribeProbe(AuthenticationMode mode)
    {
        if (!Modes.Contains(mode))
        {
            throw new UnsupportedInvocationException(SupportsAuthentication(mode).Reason!.Value);
        }

        var reference = mode == AuthenticationMode.ApiKey ? new SecretReference(ApiKeySecret, "FAKE_API_KEY") : new SecretReference(SubscriptionSecret, "FAKE_TOKEN");
        return new AgentInvocation(["/hephaestus/agents/fake/fake", "probe"], new Dictionary<string, string>(), [reference], [], ["api.fake.test"], ResultLocation.InOutputStream);
    }

    public AgentOutput Parse(IEnumerable<string> lines)
    {
        Parsed.AddRange(lines);
        return new AgentOutput([], null, [], 0);
    }

    public FailureClass? ClassifyExit(int exitCode, AgentOutput output)
    {
        ClassifiedExit = exitCode;
        return Class;
    }
}

/// <summary>A runtime that only runs probes: what a probe ends with is the test's, and anything else the runtime can do is refused, so a test shows the service asks for nothing more.</summary>
internal sealed class ProbeRuntime : IWorkspaceRuntime
{
    public List<ProbeRequest> Probes { get; } = [];

    /// <summary>What the probe ends with, or what it throws.</summary>
    public Func<ProbeRequest, ProbeResult> Run { get; set; } = _ => new ProbeResult(0, StepExitCause.Exited, ["pong"]);

    /// <summary>When set, the probe waits for what this returns before it ends; a test holds a probe open with it.</summary>
    public Func<ProbeRequest, Task>? Hold { get; set; }

    public async Task<ProbeResult> RunProbeAsync(ProbeRequest request, CancellationToken cancellationToken)
    {
        Probes.Add(request);
        if (Hold is not null)
        {
            await Hold(request);
        }

        return Run(request);
    }

    public Task<ResolvedImage> ResolveImageAsync(string image, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task EnsureTaskVolumeAsync(Guid taskId, ResolvedImage image, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<StartedStep> StartStepAsync(StepContainerRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task CheckoutAsync(CheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<CheckpointResult> CheckpointAsync(CheckpointRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<StartedStep> StartValidationAsync(Hephaestus.Core.Validation.ValidationCommandRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>A secret store that keeps its values in memory and notes every call, so that a test can show what the service stored and that it never read a value back.</summary>
internal sealed class RecordingSecrets(FakeInstallation names) : ISecretStore
{
    public Dictionary<string, string> Values { get; } = [];

    public List<string> Reads { get; } = [];

    public event Func<Task>? Changed
    {
        add { }
        remove { }
    }

    public Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        Values[name] = value;
        if (!names.SecretNames.Contains(name))
        {
            names.SecretNames.Add(name);
        }

        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        Reads.Add(name);
        return Task.FromResult(Values.GetValueOrDefault(name));
    }

    public Task<IReadOnlyList<string>> ReadAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
