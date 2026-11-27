using System.Globalization;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Readiness;

/// <summary>Why a sign-in change or a check did not happen.</summary>
public enum AgentSignInFailure
{
    /// <summary>It happened.</summary>
    None,

    /// <summary>The Agent is neither Claude Code nor Codex CLI.</summary>
    UnknownAgent,

    /// <summary>The mode is neither <c>api-key</c> nor <c>subscription</c>.</summary>
    UnknownMode,

    /// <summary>No adapter of this Agent is part of this release, so nothing can be said of how it signs in.</summary>
    NoAdapter,

    /// <summary>The adapter declares the mode unsupported; <see cref="AgentSignInResult.Detail"/> is its reason.</summary>
    UnsupportedMode,

    /// <summary>The Owner supplied no credential and none is stored for the mode.</summary>
    CredentialRequired,

    /// <summary>The credential is not an acceptable secret value; <see cref="AgentSignInResult.Refusal"/> says why.</summary>
    CredentialRefused,

    /// <summary>The Owner has not chosen how the Agent signs in.</summary>
    NotConfigured,

    /// <summary>The probe could not be run, or its finding was not recorded; this says nothing of the Agent, and <see cref="AgentSignInResult.Detail"/> is why.</summary>
    ProbeNotRun,
}

/// <summary>An Agent as the Owner sees it: how it signs in, whether the credential is stored, and what the last probe found.</summary>
/// <param name="Mode">Null while the Owner has not chosen one.</param>
/// <param name="Secret">The name of the secret that holds the credential of the chosen mode: a name, never a value; null when no mode is chosen or the Agent has no adapter.</param>
/// <param name="CredentialStored">Whether that secret exists; null when no mode is chosen.</param>
/// <param name="Capacity">What the last probe of the chosen mode's credential found; null before any.</param>
public sealed record AgentStatus(Agent Agent, AuthenticationMode? Mode, DateTimeOffset? ChosenAt, string? Secret, bool? CredentialStored, AgentCapacity? Capacity);

/// <summary>The outcome of choosing a mode or checking an Agent.</summary>
public sealed record AgentSignInResult(AgentSignInFailure Failure, AgentStatus? Status, Message? Detail = null, SecretValueRefusal Refusal = SecretValueRefusal.None)
{
    public static AgentSignInResult Failed(AgentSignInFailure failure, Message? detail = null, SecretValueRefusal refusal = SecretValueRefusal.None) => new(failure, null, detail, refusal);

    public static AgentSignInResult Done(AgentStatus status) => new(AgentSignInFailure.None, status);
}

/// <summary>
/// How each Agent signs in and whether it is ready (FR-25, FR-26, AD-16, AD-20, Story 3.10). The Owner chooses between the modes the Agent's adapter supports and supplies the
/// credential, which becomes a secret under the name the adapter's reference gives; Hephaestus presents no sign-in of its own. A check runs the adapter's probe in the runtime,
/// with no Task volume, and stores the finding on the capacity record of that credential: an Agent found unavailable has no capacity, and no step run is failed by it.
/// </summary>
public sealed class AgentSignInService
{
    /// <summary>How long a probe may take before the runtime stops it. A CLI that answers one word needs seconds; one that cannot reach its vendor waits for its own timeouts.</summary>
    public static readonly TimeSpan ProbeTimeLimit = TimeSpan.FromMinutes(2);

    /// <summary>What a probe may use: it asks one question and edits nothing.</summary>
    public static readonly StepLimits ProbeLimits = new(1000, 2L * 1024 * 1024 * 1024);

    private readonly Dictionary<Agent, IAgentAdapter> _adapters;
    private readonly IInstallationConfiguration _configuration;
    private readonly IReadStore _store;
    private readonly ISecretStore _secrets;
    private readonly IWorkspaceRuntime _runtime;
    private readonly TimeProvider _time;

    /// <summary>One probe at a time: each asks the vendor a question that costs tokens or plan usage, and two of the same credential would race to write one record.</summary>
    private readonly SemaphoreSlim _probing = new(1, 1);

    public AgentSignInService(IEnumerable<IAgentAdapter> adapters, IInstallationConfiguration configuration, IReadStore store, ISecretStore secrets, IWorkspaceRuntime runtime, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToDictionary(adapter => adapter.Agent);
        _configuration = configuration;
        _store = store;
        _secrets = secrets;
        _runtime = runtime;
        _time = time;
    }

    /// <summary>
    /// Chooses how an Agent signs in. The mode must be one its adapter supports, and the refusal is the adapter's own reason. The credential is the secret value from standard input; an empty one keeps the secret already stored, and with none stored
    /// is refused. Nothing is stored unless everything is acceptable. When a credential is stored, what an earlier probe found of the credential of this mode is forgotten, because the credential may be another.
    /// </summary>
    public async Task<AgentSignInResult> SetAuthenticationAsync(string? agentTag, string? modeTag, string? credential, CancellationToken cancellationToken)
    {
        if (!Agents.TryParse(agentTag, out var agent))
        {
            return AgentSignInResult.Failed(AgentSignInFailure.UnknownAgent);
        }

        if (!AuthenticationModes.TryParse(modeTag, out var mode))
        {
            return AgentSignInResult.Failed(AgentSignInFailure.UnknownMode);
        }

        if (!_adapters.TryGetValue(agent, out var adapter))
        {
            return AgentSignInResult.Failed(AgentSignInFailure.NoAdapter, new Message(MessageIds.AgentNoAdapter, agent.Tag()));
        }

        var support = adapter.SupportsAuthentication(mode);
        if (!support.IsSupported)
        {
            return AgentSignInResult.Failed(AgentSignInFailure.UnsupportedMode, support.Reason);
        }

        var secret = CredentialSecret(adapter, mode);
        var stored = !string.IsNullOrWhiteSpace(credential);
        if (string.IsNullOrWhiteSpace(credential))
        {
            if (!(await _store.ListSecretNamesAsync(cancellationToken)).Contains(secret.Name))
            {
                return AgentSignInResult.Failed(AgentSignInFailure.CredentialRequired, new Message(MessageIds.AgentCredentialRequired, agent.Tag(), mode.Tag(), secret.Name));
            }
        }
        else
        {
            if (SecretPolicy.CheckValue(credential) is var refusal && refusal != SecretValueRefusal.None)
            {
                return AgentSignInResult.Failed(AgentSignInFailure.CredentialRefused, refusal: refusal);
            }

            await _secrets.SetAsync(secret.Name, credential, cancellationToken);
        }

        await _configuration.SetAgentAuthenticationAsync(agent, mode, _time.GetUtcNow(), stored, cancellationToken);
        return AgentSignInResult.Done(await StatusAsync(agent, cancellationToken));
    }

    /// <summary>
    /// Runs the adapter's probe for the Agent's chosen mode and records what it found. A credential that is not stored is a finding without a probe. A probe that could not run (Docker, the image) is
    /// not a finding: nothing is recorded, because it says nothing of the Agent.
    /// </summary>
    public async Task<AgentSignInResult> CheckAsync(string? agentTag, CancellationToken cancellationToken)
    {
        if (!Agents.TryParse(agentTag, out var agent))
        {
            return AgentSignInResult.Failed(AgentSignInFailure.UnknownAgent);
        }

        if (!_adapters.TryGetValue(agent, out var adapter))
        {
            return AgentSignInResult.Failed(AgentSignInFailure.NoAdapter, new Message(MessageIds.AgentNoAdapter, agent.Tag()));
        }

        await _probing.WaitAsync(cancellationToken);
        try
        {
            return await ProbeAsync(agent, adapter, cancellationToken);
        }
        finally
        {
            _probing.Release();
        }
    }

    private async Task<AgentSignInResult> ProbeAsync(Agent agent, IAgentAdapter adapter, CancellationToken cancellationToken)
    {
        if ((await _store.ListAgentSettingsAsync(cancellationToken)).FirstOrDefault(s => s.Agent == agent) is not { } setting)
        {
            return AgentSignInResult.Failed(AgentSignInFailure.NotConfigured, new Message(MessageIds.AgentNotConfigured, agent.Tag(), string.Join('|', AuthenticationModes.Tags)));
        }

        // A mode that the adapter of this release no longer supports is the Owner's to change, not a probe that cannot be described.
        var support = adapter.SupportsAuthentication(setting.Mode);
        if (!support.IsSupported)
        {
            return AgentSignInResult.Failed(AgentSignInFailure.UnsupportedMode, support.Reason);
        }

        var invocation = adapter.DescribeProbe(setting.Mode);
        var secret = CredentialSecret(adapter, setting.Mode);
        if (!(await _store.ListSecretNamesAsync(cancellationToken)).Contains(secret.Name))
        {
            await RecordAsync(setting, AgentReadiness.Unavailable, new Message(MessageIds.AgentProbeCredentialMissing, agent.Tag(), setting.Mode.Tag(), secret.Name), cancellationToken);
            return AgentSignInResult.Done(await StatusAsync(agent, cancellationToken));
        }

        ProbeResult result;
        try
        {
            result = await _runtime.RunProbeAsync(new ProbeRequest(Identity.EntityId.New(_time), invocation, ProbeLimits, ProbeTimeLimit), cancellationToken);
        }
        catch (WorkspaceRuntimeException exception)
        {
            return AgentSignInResult.Failed(AgentSignInFailure.ProbeNotRun, new Message(MessageIds.AgentProbeNotRun, agent.Tag(), exception.Info));
        }
        catch (ImageNotUsableException exception)
        {
            return AgentSignInResult.Failed(AgentSignInFailure.ProbeNotRun, new Message(MessageIds.AgentProbeNotRun, agent.Tag(), exception.Info));
        }

        // The probe can outlast a change of how the Agent signs in. What it found is about the credential it ran with, which may not be the one that is stored now.
        if ((await _store.ListAgentSettingsAsync(cancellationToken)).FirstOrDefault(s => s.Agent == agent) != setting)
        {
            return AgentSignInResult.Failed(AgentSignInFailure.ProbeNotRun, new Message(MessageIds.AgentProbeSuperseded, agent.Tag()));
        }

        var (readiness, reason) = Read(adapter, agent, result);
        await RecordAsync(setting, readiness, reason, cancellationToken);
        return AgentSignInResult.Done(await StatusAsync(agent, cancellationToken));
    }

    /// <summary>Every Agent with how it signs in and what its last probe found, in the order of <see cref="Agent"/>.</summary>
    public async Task<IReadOnlyList<AgentStatus>> ListAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.ListAgentSettingsAsync(cancellationToken);
        var capacities = await _store.ListAgentCapacitiesAsync(cancellationToken);
        var names = await _store.ListSecretNamesAsync(cancellationToken);
        return [.. Enum.GetValues<Agent>().Select(agent => Status(agent, settings, capacities, names))];
    }

    /// <summary>What a probe's end says of the Agent: ready only when the adapter positively reads a result that is not an error; otherwise unavailable, with a reason that holds no text the CLI printed.</summary>
    private static (AgentReadiness Readiness, Message? Reason) Read(IAgentAdapter adapter, Agent agent, ProbeResult result)
    {
        switch (result.Cause)
        {
            case StepExitCause.Timeout:
                return (AgentReadiness.Unavailable, new Message(MessageIds.AgentProbeTimedOut, agent.Tag(), ((int)ProbeTimeLimit.TotalSeconds).ToString(CultureInfo.InvariantCulture)));
            case StepExitCause.Exited:
                break;
            default:
                return (AgentReadiness.Unavailable, new Message(MessageIds.AgentProbeLost, agent.Tag(), result.Cause.Tag()));
        }

        var exitCode = (int)Math.Clamp(result.ExitCode, int.MinValue, int.MaxValue);
        var failure = adapter.ClassifyExit(exitCode, adapter.Parse(result.Lines));
        return failure switch
        {
            null => (AgentReadiness.Ready, null),
            FailureClass.AgentError => (AgentReadiness.Unavailable, new Message(MessageIds.AgentProbeError, agent.Tag(), exitCode.ToString(CultureInfo.InvariantCulture))),
            _ => (AgentReadiness.Unavailable, new Message(MessageIds.AgentProbeUnknown, agent.Tag(), exitCode.ToString(CultureInfo.InvariantCulture))),
        };
    }

    private Task RecordAsync(AgentSetting setting, AgentReadiness readiness, Message? reason, CancellationToken cancellationToken) =>
        _configuration.RecordAgentProbeAsync(setting.Agent, setting.Mode, readiness, reason?.Id, [.. (reason?.Arguments ?? []).Select(argument => Convert.ToString(argument, CultureInfo.InvariantCulture) ?? string.Empty)], _time.GetUtcNow(), cancellationToken);

    private async Task<AgentStatus> StatusAsync(Agent agent, CancellationToken cancellationToken) =>
        Status(agent, await _store.ListAgentSettingsAsync(cancellationToken), await _store.ListAgentCapacitiesAsync(cancellationToken), await _store.ListSecretNamesAsync(cancellationToken));

    private AgentStatus Status(Agent agent, IReadOnlyList<AgentSetting> settings, IReadOnlyList<AgentCapacity> capacities, IReadOnlyList<string> secretNames)
    {
        if (settings.FirstOrDefault(s => s.Agent == agent) is not { } setting)
        {
            return new AgentStatus(agent, null, null, null, null, null);
        }

        var secret = _adapters.TryGetValue(agent, out var adapter) ? CredentialSecret(adapter, setting.Mode).Name : null;
        return new AgentStatus(agent, setting.Mode, setting.ChosenAt, secret, secret is not null && secretNames.Contains(secret), capacities.FirstOrDefault(c => c.Agent == agent && c.Mode == setting.Mode));
    }

    /// <summary>The one secret the adapter needs in this mode, which the probe's invocation names as it does a step's (AD-10).</summary>
    private static SecretReference CredentialSecret(IAgentAdapter adapter, AuthenticationMode mode) => adapter.DescribeProbe(mode).SecretReferences.Single();
}
