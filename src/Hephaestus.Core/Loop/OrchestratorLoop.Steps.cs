using System.Text.Json;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Context;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Failures;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Methodologies;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.PullRequests;
using Hephaestus.Core.Results;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Settings;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Loop;

/// <summary>
/// The steps of a <c>Running</c> Task (FR-17 to FR-23, FR-31, FR-36): the Primary Agent performs <c>Implementation</c> on a fresh checkout, its work is checkpointed and pushed, each of the Project's Validation
/// commands runs on a fresh checkout of the pushed tip, and on a pass the pull request is journaled for publication (Story 3.13). Every decision is made from the step runs, the journal, and the snapshot, never from
/// memory, so that a tick can stop between any two of them. The Task holds the single slot until publication moves it out of <c>Running</c>.
/// </summary>
public sealed partial class OrchestratorLoop
{
    /// <summary>How many times one tick carries a Task a step further, so that the loop beats and the Signals behind are not held back.</summary>
    private const int MaximumAdvancesPerTick = 12;

    private async Task AdvanceAsync(TaskRecord started, EffectsRun run, CancellationToken cancellationToken)
    {
        for (var advances = 0; advances < MaximumAdvancesPerTick; advances++)
        {
            if (await reads.FindTaskAsync(started.Id, cancellationToken) is not { State: TaskState.Running } task || !await AdvanceOnceAsync(task, run, cancellationToken))
            {
                return;
            }

            // A checkout or a checkpoint is a container that is waited for, so the loop is shown to be alive between the steps of a Task.
            heartbeat.Beat();
        }
    }

    /// <summary>Does the next thing the records ask for. Returns whether it changed them in a way that lets the Task go further at once.</summary>
    private async Task<bool> AdvanceOnceAsync(TaskRecord task, EffectsRun run, CancellationToken cancellationToken)
    {
        if (await reads.FindTaskSnapshotAsync(task.Id, cancellationToken) is not { } snapshot)
        {
            return false;
        }

        var steps = await reads.ListStepRunsAsync(task.Id, cancellationToken);
        if (steps.Count == 0)
        {
            return false;
        }

        var last = steps[^1];
        return last.Status switch
        {
            StepRunStatus.Planned => await StartStepAsync(task, snapshot, last, run, cancellationToken),
            StepRunStatus.Ended when last.IsValidation => await AfterValidationAsync(task, snapshot, steps, run, cancellationToken),
            StepRunStatus.Ended => await AfterImplementationAsync(task, snapshot, last, run, cancellationToken),
            StepRunStatus.Checkpointed => await AfterCheckpointAsync(task, snapshot, steps, run, cancellationToken),
            _ => false,
        };
    }

    // The step of an Agent or one command of Validation: its container is asked for, and the loop does not wait for it (AD-18).

    private async Task<bool> StartStepAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, EffectsRun run, CancellationToken cancellationToken)
    {
        try
        {
            // Before any step, the Task branch on GitHub is looked at (AD-9): where someone else has moved it, no step runs on it, and the Task is held for the Owner.
            if (branches is not null && await CheckBranchAsync(task, snapshot, step, run, cancellationToken) is { } held)
            {
                return held;
            }

            var bundle = await BundleOfAsync(task, step, step.TipBefore, cancellationToken);
            if (bundle is null)
            {
                await FailStepAsync(task, step, FailureClass.InfrastructureError, "the commit to check out could not be bundled", run, cancellationToken);
                return false;
            }

            var limits = StepLimits.Resolve(snapshot.Configuration.Limits, await reads.GetLimitsAsync(cancellationToken));
            if (step.IsValidation)
            {
                var command = snapshot.Configuration.Validation[step.CommandIndex!.Value];
                var request = new ValidationCommandRequest(task.Id, step.Id, task.Branch, bundle, snapshot.Image, command, limits, snapshot.Configuration.AllowedEgressHosts, step.Deadline);
                await runtime!.EnsureTaskVolumeAsync(task.Id, snapshot.Image, RunKeys.Volume(task.Id, step.Id), cancellationToken);
                await runtime.StartValidationAsync(request, RunKeys.Container(task.Id, step.Id), cancellationToken);
            }
            else if (!await StartAgentStepAsync(task, snapshot, step, bundle, limits, run, cancellationToken))
            {
                return false;
            }

            await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Started), cancellationToken);
            return false;
        }
        catch (Exception exception) when (exception is WorkspaceRuntimeException or ImageNotUsableException or ArgumentException or MethodologyException or UnsupportedInvocationException)
        {
            await FailStepAsync(task, step, FailureClass.InfrastructureError, exception.Message, run, cancellationToken);
            return false;
        }
    }

    private async Task<bool> StartAgentStepAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, BundleReference bundle, StepLimits limits, EffectsRun run, CancellationToken cancellationToken)
    {
        var configuration = snapshot.Configuration;
        var adapter = _adapters.FirstOrDefault(candidate => candidate.Agent == step.Agent);
        var setting = (await reads.ListAgentSettingsAsync(cancellationToken)).FirstOrDefault(candidate => candidate.Agent == step.Agent);
        if (adapter is null || setting is null || step.Model is null)
        {
            await FailStepAsync(task, step, FailureClass.InfrastructureError, "the Agent of the step can no longer be driven", run, cancellationToken);
            return false;
        }

        var project = (await reads.ListProjectsAsync(includeRemoved: true, cancellationToken)).First(candidate => candidate.RepositoryId == task.RepositoryId);
        var assembled = await StepContextAssembler.AssembleAsync(
            methodologies!,
            new StepContextInput(
                task.Id,
                step.Id,
                task.RepositoryId,
                task.IssueNumber,
                task.Branch,
                snapshot.CommitId,
                project.BaseBranch,
                step.Kind,
                step.Round,
                await TextLanguageAsync(configuration, cancellationToken),
                configuration.Methodology,
                await reads.FindIssueSnapshotAsync(task.Id, cancellationToken),
                await reads.ListAdmittedCommentsAsync(task.Id, cancellationToken)),
            cancellationToken);
        var invocation = adapter.DescribeInvocation(new InvocationRequest(step.Id, ContextFiles.Instruction, setting.Mode, step.Model, step.Effort, assembled.ResultSchema));

        await runtime!.EnsureTaskVolumeAsync(task.Id, snapshot.Image, RunKeys.Volume(task.Id, step.Id), cancellationToken);
        await runtime.CheckoutAsync(new CheckoutRequest(task.Id, step.Id, task.Branch, bundle), RunKeys.Checkout(task.Id, step.Id), cancellationToken);
        var container = new StepContainerRequest(
            task.Id,
            step.Id,
            snapshot.Image,
            invocation.Command,
            invocation.Environment,
            limits,
            [.. assembled.Context, .. invocation.Files.Where(file => assembled.Context.All(known => known.Path != file.Path))],
            assembled.Methodology,
            step.Deadline,
            EgressPolicy.AllowedHosts(configuration.AllowedEgressHosts, invocation.EgressHosts),
            invocation.SecretReferences);
        await runtime.StartStepAsync(container, RunKeys.Container(task.Id, step.Id), cancellationToken);
        return true;
    }

    /// <summary>
    /// Fetches the Task branch and says whether the step may run on it. Null when it may: the branch is where Hephaestus left it, or is not there because nothing was pushed. A branch that moved holds the Task: an Escalation names it and the Task waits until a Trusted User adopts the remote tip
    /// (the <c>confirm</c> command is Story 3.18's), and the step run ends with no failure; one that could not be read ends the step as an infrastructure error. Returns false, that nothing more is to be done now, in both cases.
    /// </summary>
    private async Task<bool?> CheckBranchAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, EffectsRun run, CancellationToken cancellationToken)
    {
        var check = await branches!.CheckBranchAsync(new BranchCheckRequest(task.RepositoryId, task.Branch, step.TipBefore, snapshot.CommitId), cancellationToken);
        switch (check.State)
        {
            case BranchState.InSync:
                return null;
            case BranchState.Moved:
                var plan = EscalationPath.ForBlockedPush(task, PushCheck.RemoteMoved, check.Detail, EffectKeys.StepCause(step.Id), time);
                if (plan is not null && await work.EscalateAsync(plan, cancellationToken))
                {
                    run.Moved[task.Id] = TaskState.WaitingForOwner;
                    run.TaskProblems.Add($"Task {task.Id} of issue #{task.IssueNumber} waits for the Owner: its branch is not where Hephaestus left it.");
                }

                await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, new StepRunEnd(time.GetUtcNow(), null, StepExitCause.Lost, null, StepOutcomes.BranchMoved, null)), cancellationToken);
                return false;
            default:
                await FailStepAsync(task, step, FailureClass.InfrastructureError, "the Task branch could not be read from GitHub", run, cancellationToken);
                return false;
        }
    }

    /// <summary>The bundle of a commit of the Project's mirror, which the next checkout reads once (AD-9); null when the mirror cannot be brought up to date or lacks the commit.</summary>
    private async Task<BundleReference?> BundleOfAsync(TaskRecord task, StepRun step, string commit, CancellationToken cancellationToken)
    {
        if ((await mirrors!.FetchAsync(task.RepositoryId, cancellationToken)).Failure != MirrorFailure.None)
        {
            return null;
        }

        return (await mirrors.CreateBundleAsync(new BundleRequest(task.RepositoryId, commit, task.Id, step.Id), cancellationToken)).Bundle;
    }

    private async Task<Language> TextLanguageAsync(ProjectConfiguration configuration, CancellationToken cancellationToken) =>
        LanguageResolver.ForText(task: configuration.Language, project: null, LanguageResolver.Installation(InstallationSettings.StoredLanguage(await reads.ListSettingsAsync(cancellationToken)), fallbackLanguage));

    // The end of an Implementation: the Signal that reported it recorded what the Agent said; here its work is kept as one commit and pushed (AD-9).

    private async Task<bool> AfterImplementationAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, EffectsRun run, CancellationToken cancellationToken)
    {
        if (step.Outcome == StepOutcomes.NeedsDecision)
        {
            // The Agent asked the Owner something (AD-13): the question is the Escalation, which moves the Task out of Running, so the next Implementation is a step of its own once it is answered.
            if (step.Result is not null && JsonSerializer.Deserialize(step.Result, ResultJson.Default.ImplementationResult) is { } result)
            {
                await RaiseEscalationAsync(task, StepOutcomes.Request(result), EffectKeys.StepCause(step.Id), cancellationToken);
            }

            return false;
        }

        try
        {
            if (await reads.GetGitHubAppSlugAsync(cancellationToken) is not { } slug)
            {
                await FailStepAsync(task, step, FailureClass.InfrastructureError, "GitHub is not connected, so the work cannot be committed under Hephaestus's identity", run, cancellationToken);
                return false;
            }

            var parent = await BundleOfAsync(task, step, step.TipBefore, cancellationToken);
            if (parent is null)
            {
                await FailStepAsync(task, step, FailureClass.InfrastructureError, "the commit the work was started from could not be bundled", run, cancellationToken);
                return false;
            }

            var commit = CheckpointCommit.Compose(CommitIdentity.ForSlug(slug), await TextLanguageAsync(snapshot.Configuration, cancellationToken), step.Kind, task.Id, step.Id, step.Agent!.Value, step.Model!, step.Effort);
            var checkpoint = await runtime!.CheckpointAsync(new CheckpointRequest(task.Id, step.Id, step.Kind, task.Branch, parent, commit), RunKeys.Checkpoint(task.Id, step.Id), cancellationToken);
            if (checkpoint.Bundle is not { } bundle)
            {
                // The Agent left the tree as it found it. With nothing ever pushed there is nothing to publish, so the Task fails; with earlier work pushed, that work is what Validation checks.
                if (step.TipBefore == snapshot.CommitId)
                {
                    await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Concluded, Move: FailedMove(task, step), Failure: FailureClass.AgentError), cancellationToken);
                    run.Moved[task.Id] = TaskState.Failed;
                    run.TaskProblems.Add($"Task {task.Id} of issue #{task.IssueNumber} failed: the Agent made no change.");
                    return false;
                }

                return await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Checkpointed, TipAfter: step.TipBefore), cancellationToken);
            }

            var push = PushIntent.For(step.Id, bundle, snapshot.CommitId).ToNewEffect(task.Id);
            return await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Checkpointed, TipAfter: bundle.Tip, Effects: [push]), cancellationToken);
        }
        catch (Exception exception) when (exception is WorkspaceRuntimeException or ArgumentException)
        {
            await FailStepAsync(task, step, FailureClass.InfrastructureError, exception.Message, run, cancellationToken);
            return false;
        }
    }

    // A checkpointed Implementation whose push is made: Validation begins on the pushed tip, one command at a time (FR-31).

    private async Task<bool> AfterCheckpointAsync(TaskRecord task, TaskSnapshot snapshot, IReadOnlyList<StepRun> steps, EffectsRun run, CancellationToken cancellationToken)
    {
        var step = steps[^1];
        var tip = step.TipAfter ?? step.TipBefore;
        if (tip != step.TipBefore)
        {
            // The push is an effect of its own, made in the effects phase; the commit it moves the branch to is what Validation checks, so it waits until the push is made.
            var journal = await reads.ListJournalAsync(task.Id, cancellationToken);
            var pushed = journal.Any(entry => entry.Entry == JournalEntryKind.Result && entry.Effect.Kind == EffectKind.Push && entry.Effect.Cause == EffectKeys.StepCause(step.Id) && entry.Result is EffectResult.Applied or EffectResult.AlreadyInPlace);
            if (!pushed)
            {
                return false;
            }
        }

        return await PlanValidationAsync(task, snapshot, step.Round, tip, commandIndex: 0, cancellationToken);
    }

    private async Task<bool> PlanValidationAsync(TaskRecord task, TaskSnapshot snapshot, int round, string tip, int commandIndex, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var limit = ValidationRun.CommandLimit(snapshot.Configuration.Limits, await reads.GetLimitsAsync(cancellationToken));
        var step = new NewStepRun(EntityId.New(time), task.Id, StepKind.Validation, round, now + limit, now, tip, CommandIndex: commandIndex);
        return await work.PlanStepRunAsync(step, cancellationToken);
    }

    // A command of Validation that passed: the next one runs, or, when all have, the pull request is journaled for publication. A command that did not pass ended the Task when its end was applied.

    private async Task<bool> AfterValidationAsync(TaskRecord task, TaskSnapshot snapshot, IReadOnlyList<StepRun> steps, EffectsRun run, CancellationToken cancellationToken)
    {
        var anchor = steps.Last(step => !step.IsValidation);
        var validation = ValidationRun.Begin(snapshot.Configuration.Validation);
        foreach (var step in steps.Where(step => step.IsValidation && step.Sequence > anchor.Sequence).OrderBy(step => step.Sequence))
        {
            var index = step.CommandIndex!.Value;
            validation = validation.Record(ValidationCommandResult.Ended(index, snapshot.Configuration.Validation[index], step.Id, step.ExitCode, step.Cause ?? StepExitCause.Lost, step.Duration ?? TimeSpan.Zero));
        }

        var tip = steps[^1].TipBefore;
        if (!validation.IsDecided)
        {
            return await PlanValidationAsync(task, snapshot, anchor.Round, tip, validation.NextIndex!.Value, cancellationToken);
        }

        if (validation.Outcome != ValidationOutcome.Passed || ImplementationResultOf(anchor) is not { } result)
        {
            return false;
        }

        var title = (await reads.FindIssueSnapshotAsync(task.Id, cancellationToken))?.Title ?? task.Branch;
        var implementer = new PullRequestAgent(anchor.Agent!.Value.Tag(), anchor.Model!, anchor.Effort?.Tag() ?? "default");
        var facts = PullRequestFacts.From(title, result, implementer, validation);
        var publication = new PullRequestIntent(anchor.Id, tip).ToNewEffect(task.Id, facts);
        await work.ChangeStepRunAsync(new StepRunChange(steps[^1].Id, StepRunStatus.Concluded, Effects: [publication]), cancellationToken);
        return false;
    }

    private static ImplementationResult? ImplementationResultOf(StepRun step)
    {
        try
        {
            return step.Result is null ? null : JsonSerializer.Deserialize(step.Result, ResultJson.Default.ImplementationResult);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A step that could not be carried out ends the Task: Story 3.16 classifies and retries; until then, the Task fails and keeps its branch.

    private static TaskMove FailedMove(TaskRecord task, StepRun step) =>
        new(TaskState.Running, TaskState.Failed, [new NewEffect(task.Id, EffectKeys.StepCause(step.Id), EffectKind.Label, TaskState.Failed.Tag())]);

    private async Task FailStepAsync(TaskRecord task, StepRun step, FailureClass failure, string detail, EffectsRun run, CancellationToken cancellationToken)
    {
        var end = new StepRunEnd(time.GetUtcNow(), null, StepExitCause.Lost, failure, null, null);
        if (await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, end, Move: FailedMove(task, step)), cancellationToken))
        {
            run.Moved[task.Id] = TaskState.Failed;
            run.TaskProblems.Add($"Task {task.Id} of issue #{task.IssueNumber} failed in {step.Kind.Tag()} ({failure.Tag()}): {detail}.");
        }
    }

    // The end of a container, as the runtime reported it (AD-18): the loop gives the stored stream to the adapter, validates the result, and writes the step run.

    private async Task<SignalApplication> DecideStepExitedAsync(StoredSignal stored, CancellationToken cancellationToken)
    {
        var applied = new SignalApplication(stored.Sequence, SignalStatus.Applied);
        var signal = stored.Signal;
        if (signal.StepRunId is not { } stepRunId || signal.Cause is not { } cause || await reads.FindStepRunAsync(stepRunId, cancellationToken) is not { } step)
        {
            // A container of a probe, a helper, or a step run this loop does not know: nothing is recorded for it.
            return applied;
        }

        applied = applied with { TaskId = step.TaskId };
        if (step.Status is not (StepRunStatus.Planned or StepRunStatus.Started) || await reads.FindTaskAsync(step.TaskId, cancellationToken) is not { } task)
        {
            return applied;
        }

        var at = time.GetUtcNow();
        var end = step.IsValidation
            ? await EndOfValidationAsync(step, signal, cause, at, cancellationToken)
            : await EndOfAgentStepAsync(step, signal, cause, at, cancellationToken);
        var move = end.Failure is not null && task.State == TaskState.Running ? FailedMove(task, step) : null;
        return applied with { StepRun = new StepRunChange(step.Id, StepRunStatus.Ended, end, Move: move) };
    }

    private async Task<StepRunEnd> EndOfValidationAsync(StepRun step, Signal signal, StepExitCause cause, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var snapshot = await reads.FindTaskSnapshotAsync(step.TaskId, cancellationToken);
        if (snapshot is null || step.CommandIndex is not { } index || index >= snapshot.Configuration.Validation.Count)
        {
            return new StepRunEnd(at, signal.ExitCode, cause, FailureClass.InfrastructureError, null, null);
        }

        var result = ValidationCommandResult.Ended(index, snapshot.Configuration.Validation[index], step.Id, signal.ExitCode, cause, at - (step.StartedAt ?? step.PlannedAt));
        var failure = result.Status switch
        {
            ValidationCommandStatus.Passed => (FailureClass?)null,
            ValidationCommandStatus.Failed => FailureClass.ValidationFailure,
            ValidationCommandStatus.TimedOut => FailureClass.Timeout,
            _ => FailureClass.InfrastructureError,
        };
        return new StepRunEnd(at, signal.ExitCode, cause, failure, result.Status.Tag(), null);
    }

    /// <summary>
    /// How an Agent's step ended, decided in the order AD-11 gives: the runtime's cause first, then the adapter's reading of a CLI that ended by itself, then Core's reading of the result, which is validated by strict deserialization and
    /// is never interpreted when it is missing or invalid (AD-13).
    /// </summary>
    private async Task<StepRunEnd> EndOfAgentStepAsync(StepRun step, Signal signal, StepExitCause cause, DateTimeOffset at, CancellationToken cancellationToken)
    {
        StepRunEnd Ended(FailureClass? failure, string? outcome = null, string? result = null) => new(at, signal.ExitCode, cause, failure, outcome, result);

        switch (cause)
        {
            case StepExitCause.Timeout:
                return Ended(FailureClass.Timeout);
            case StepExitCause.OutOfMemory or StepExitCause.Lost:
                return Ended(FailureClass.InfrastructureError);
        }

        var adapter = _adapters.FirstOrDefault(candidate => candidate.Agent == step.Agent);
        if (adapter is null)
        {
            return Ended(FailureClass.Unknown);
        }

        AgentOutput parsed;
        await using (var stream = await output!.OpenReadAsync(step.Id, cancellationToken))
        {
            parsed = stream is null ? new AgentOutput([], null, [], 0) : adapter.Parse(ReadLines(stream));
        }

        if (adapter.ClassifyExit(signal.ExitCode ?? -1, parsed) is { } classified)
        {
            return Ended(classified);
        }

        return StepOutcomes.Read(parsed.Result?.StructuredOutput) is { } read
            ? Ended(null, read.Outcome == StepOutcome.Completed ? StepOutcomes.Completed : StepOutcomes.NeedsDecision, parsed.Result!.StructuredOutput)
            : Ended(FailureClass.AgentError);
    }

    private static IEnumerable<string> ReadLines(Stream stream)
    {
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
