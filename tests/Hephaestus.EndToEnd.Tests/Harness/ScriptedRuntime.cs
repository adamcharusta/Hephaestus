using System.Text;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Output;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.EndToEnd.Tests.Harness;

/// <summary>What a scripted container leaves: how its process ended and what it printed.</summary>
internal sealed record ScriptedEnd(int ExitCode, params string[] Output);

/// <summary>
/// The runtime of a harness that has no Docker Engine (Story 3.14, AR-37): it starts nothing and, as the real one's watcher does, keeps the output of each container through the masker's place, the output store, and appends the one step-exited Signal that
/// ends it, so that the loop under test hears of a step as it does in production. What a container "prints" and how it "ends" is the test's: the scripted Agent's step, and each command of Validation.
/// </summary>
internal sealed class ScriptedRuntime(ISignalInbox inbox, IOutputStore output, TimeProvider time) : IWorkspaceRuntime
{
    public static readonly ResolvedImage Image = new("ubuntu:24.04", "sha256:" + new string('7', 64), AgentArchitecture.X64, CLibrary.Glibc);

    /// <summary>What the step of the Agent ends with, from the request that started it.</summary>
    public Func<StepContainerRequest, ScriptedEnd> Agent { get; set; } = _ => new ScriptedEnd(0);

    /// <summary>What a command of Validation ends with, from the command.</summary>
    public Func<string, ScriptedEnd> Validation { get; set; } = _ => new ScriptedEnd(0);

    public List<StepContainerRequest> AgentSteps { get; } = [];

    public List<ValidationCommandRequest> Commands { get; } = [];

    public List<CheckoutRequest> Checkouts { get; } = [];

    public Task<ResolvedImage> ResolveImageAsync(string image, CancellationToken cancellationToken) => Task.FromResult(Image with { Reference = image });

    public Task EnsureTaskVolumeAsync(Guid taskId, ResolvedImage image, string idempotencyKey, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CheckoutAsync(CheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        Checkouts.Add(request);
        return Task.CompletedTask;
    }

    public async Task<StartedStep> StartStepAsync(StepContainerRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        AgentSteps.Add(request);
        await EndAsync(request.TaskId, request.StepRunId, Agent(request), cancellationToken);
        return new StartedStep("scripted-" + request.StepRunId.ToString("N")[..8], "hephaestus-scripted", Created: true);
    }

    public async Task<StartedStep> StartValidationAsync(ValidationCommandRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        Commands.Add(request);
        await EndAsync(request.TaskId, request.StepRunId, Validation(request.Command), cancellationToken);
        return new StartedStep("scripted-" + request.StepRunId.ToString("N")[..8], "hephaestus-scripted", Created: true);
    }

    public Task<CheckpointResult> CheckpointAsync(CheckpointRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(CheckpointResult.Made(new BundleReference(BundleReference.CheckpointPath(request.TaskId, request.StepRunId), new string('c', 64), new string('b', 40), request.Parent.Tip)));

    public Task<ProbeResult> RunProbeAsync(ProbeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    private async Task EndAsync(Guid taskId, Guid stepRunId, ScriptedEnd end, CancellationToken cancellationToken)
    {
        await using (var writer = await output.BeginAsync(new OutputRun(taskId, stepRunId, "scripted-" + stepRunId.ToString("N")[..8]), cancellationToken))
        {
            await writer.WriteAsync(Encoding.UTF8.GetBytes(string.Join('\n', end.Output) + (end.Output.Length > 0 ? "\n" : string.Empty)), cancellationToken);
            await writer.CompleteAsync(cancellationToken);
        }

        await inbox.AppendAsync([Signal.StepExited(stepRunId, end.ExitCode, StepExitCause.Exited, time.GetUtcNow())], cancellationToken);
    }
}
