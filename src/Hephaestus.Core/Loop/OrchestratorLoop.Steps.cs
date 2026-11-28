using System.Text.Json;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Context;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Failures;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Limits;
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
/// memory, so that a tick can stop between any two of them. The Task holds the single slot until publication moves it out of <c>Running</c>. A step that fails is classified, and one that fails in a class that spends the step retry bound is attempted again
/// as a new step run on a fresh checkout of the last pushed tip, until the bound or the Task's duration limit is spent and the Task fails with a comment (Story 3.16).
/// </summary>
public sealed partial class OrchestratorLoop
{
    /// <summary>How many times one tick carries a Task a step further, so that the loop beats and the Signals behind are not held back.</summary>
    private const int MaximumAdvancesPerTick = 12;

    /// <summary>How many times one tick after another may fail to start a step, or to keep the work of one that failed, before the step ends as an infrastructure error, or its work is left behind; the step retry bound then decides what follows.</summary>
    public const int MaximumStartAttempts = 5;

    private readonly Dictionary<Guid, int> _startFailures = [];

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
            StepRunStatus.Ended or StepRunStatus.Checkpointed when last.Failure is not null => await AfterFailureAsync(task, snapshot, last, run, cancellationToken),
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
                return await FailStepAsync(task, snapshot, step, FailureClass.InfrastructureError, "the commit to check out could not be bundled", run, cancellationToken);
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

            _startFailures.Remove(step.Id);
            await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Started), cancellationToken);
            return false;
        }
        catch (Exception exception) when (exception is WorkspaceRuntimeException or ImageNotUsableException or ArgumentException or MethodologyException or UnsupportedInvocationException)
        {
            return await FailStepAsync(task, snapshot, step, FailureClass.InfrastructureError, exception.Message, run, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && CountStartFailure(step) >= MaximumStartAttempts)
        {
            // A runtime or a GitHub that fails again and again is not waited for without end: the step ends as an infrastructure error, which the step retry bound then attempts again or ends the Task for.
            _startFailures.Remove(step.Id);
            return await FailStepAsync(task, snapshot, step, FailureClass.InfrastructureError, $"it could not be started in {MaximumStartAttempts} attempts ({exception.GetType().Name})", run, cancellationToken);
        }
    }

    /// <summary>How many times in a row a step run's start met a failure that is not its own; kept in memory, so that a restart gives it a fresh count.</summary>
    private int CountStartFailure(StepRun step) => _startFailures[step.Id] = _startFailures.GetValueOrDefault(step.Id) + 1;

    private async Task<bool> StartAgentStepAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, BundleReference bundle, StepLimits limits, EffectsRun run, CancellationToken cancellationToken)
    {
        var configuration = snapshot.Configuration;
        var adapter = _adapters.FirstOrDefault(candidate => candidate.Agent == step.Agent);
        var setting = (await reads.ListAgentSettingsAsync(cancellationToken)).FirstOrDefault(candidate => candidate.Agent == step.Agent);
        if (adapter is null || setting is null || step.Model is null)
        {
            await FailStepAsync(task, snapshot, step, FailureClass.InfrastructureError, "the Agent of the step can no longer be driven", run, cancellationToken);
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
    /// (the <c>confirm</c> command is Story 3.18's), and the step run ends with no failure; one that could not be read ends the step as an infrastructure error, which the step retry bound decides on.
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
                if (plan is null || !await work.EscalateAsync(plan, cancellationToken))
                {
                    // The Task could not be held, so the step is not ended as one that never ran: the next tick looks at the branch again.
                    return false;
                }

                run.Moved[task.Id] = TaskState.WaitingForOwner;
                run.TaskProblems.Add($"Task {task.Id} of issue #{task.IssueNumber} waits for the Owner: its branch is not where Hephaestus left it.");
                await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, new StepRunEnd(time.GetUtcNow(), null, StepExitCause.Lost, null, StepOutcomes.BranchMoved, null)), cancellationToken);
                return false;
            default:
                return await FailStepAsync(task, snapshot, step, FailureClass.InfrastructureError, "the Task branch could not be read from GitHub", run, cancellationToken);
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
            if (ImplementationResultOf(step) is { } result)
            {
                await RaiseEscalationAsync(task, StepOutcomes.Request(result), EffectKeys.StepCause(step.Id), cancellationToken);
                return false;
            }

            return await FailStepAsync(task, snapshot, step, FailureClass.AgentError, "the result that asks for a decision cannot be read", run, cancellationToken);
        }

        try
        {
            if (await reads.GetGitHubAppSlugAsync(cancellationToken) is not { } slug)
            {
                return await FailStepAsync(task, snapshot, step, FailureClass.InfrastructureError, "GitHub is not connected, so the work cannot be committed under Hephaestus's identity", run, cancellationToken);
            }

            var parent = await BundleOfAsync(task, step, step.TipBefore, cancellationToken);
            if (parent is null)
            {
                return await FailCheckpointAsync(task, snapshot, step, "the commit the work was started from could not be bundled", run, cancellationToken);
            }

            var checkpoint = await CheckpointAsync(task, snapshot, step, slug, parent, cancellationToken);
            if (checkpoint.Bundle is not { } bundle)
            {
                // The Agent left the tree as it found it. With nothing ever pushed there is nothing to publish, so the Task fails; with earlier work pushed, that work is what Validation checks.
                if (step.TipBefore == snapshot.CommitId)
                {
                    var bounds = await ReadBoundsAsync(task, snapshot, cancellationToken);
                    await EndTaskAsync(task, step, FailureClass.AgentError, FailureFactsOf(task, snapshot, step, FailureClass.AgentError, StepFailureEnd.NoChange, bounds), run, cancellationToken);
                    return false;
                }

                return await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Checkpointed, TipAfter: step.TipBefore), cancellationToken);
            }

            var push = PushIntent.For(step.Id, bundle, snapshot.CommitId).ToNewEffect(task.Id);
            return await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Checkpointed, TipAfter: bundle.Tip, Effects: [push]), cancellationToken);
        }
        catch (Exception exception) when (exception is WorkspaceRuntimeException or ArgumentException)
        {
            return await FailCheckpointAsync(task, snapshot, step, exception.Message, run, cancellationToken);
        }
    }

    /// <summary>
    /// The work of a step that ended well could not be kept. What it left is in the Task's volume, so the checkpoint is tried again by each tick, up to <see cref="MaximumStartAttempts"/> times, before the step is failed and attempted again from the
    /// last pushed tip, which loses it.
    /// </summary>
    private async Task<bool> FailCheckpointAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, string detail, EffectsRun run, CancellationToken cancellationToken)
    {
        if (CountStartFailure(step) < MaximumStartAttempts)
        {
            return false;
        }

        _startFailures.Remove(step.Id);
        return await FailStepAsync(task, snapshot, step, FailureClass.InfrastructureError, detail, run, cancellationToken);
    }

    /// <summary>Keeps the work of an Agent's step as one commit on top of the tip its checkout was made from (AD-9).</summary>
    private async Task<CheckpointResult> CheckpointAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, string slug, BundleReference parent, CancellationToken cancellationToken)
    {
        var commit = CheckpointCommit.Compose(CommitIdentity.ForSlug(slug), await TextLanguageAsync(snapshot.Configuration, cancellationToken), step.Kind, task.Id, step.Id, step.Agent!.Value, step.Model!, step.Effort);
        return await runtime!.CheckpointAsync(new CheckpointRequest(task.Id, step.Id, step.Kind, task.Branch, parent, commit), RunKeys.Checkpoint(task.Id, step.Id), cancellationToken);
    }

    // A checkpointed Implementation whose push is made: Validation begins on the pushed tip, one command at a time (FR-31).

    private async Task<bool> AfterCheckpointAsync(TaskRecord task, TaskSnapshot snapshot, IReadOnlyList<StepRun> steps, EffectsRun run, CancellationToken cancellationToken)
    {
        var step = steps[^1];
        var tip = step.TipAfter ?? step.TipBefore;

        // The push is an effect of its own, made in the effects phase; the commit it moves the branch to is what Validation checks, so it waits until the push is made.
        if (!await IsPushedAsync(task, step, cancellationToken))
        {
            return false;
        }

        return await PlanValidationAsync(task, snapshot, step.Round, tip, commandIndex: 0, cancellationToken);
    }

    /// <summary>Whether what the step's checkpoint moved the Task branch to is there: a step that left the tree as it was has nothing to push, and one that did has its push journaled as made.</summary>
    private async Task<bool> IsPushedAsync(TaskRecord task, StepRun step, CancellationToken cancellationToken)
    {
        if ((step.TipAfter ?? step.TipBefore) == step.TipBefore)
        {
            return true;
        }

        var journal = await reads.ListJournalAsync(task.Id, cancellationToken);
        return journal.Any(entry => entry.Entry == JournalEntryKind.Result && entry.Effect.Kind == EffectKind.Push && entry.Effect.Cause == EffectKeys.StepCause(step.Id) && entry.Result is EffectResult.Applied or EffectResult.AlreadyInPlace);
    }

    private async Task<bool> PlanValidationAsync(TaskRecord task, TaskSnapshot snapshot, int round, string tip, int commandIndex, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var limits = await reads.GetLimitsAsync(cancellationToken);
        var deadline = ValidationDeadline(task, snapshot, limits, now);
        var step = new NewStepRun(EntityId.New(time), task.Id, StepKind.Validation, round, deadline, now, tip, CommandIndex: commandIndex);
        return await work.PlanStepRunAsync(step, cancellationToken);
    }

    /// <summary>When a command of Validation is stopped: after its own limit, and never after the moment the Task's time in <c>Running</c> reaches its limit (NFR-5).</summary>
    private static DateTimeOffset ValidationDeadline(TaskRecord task, TaskSnapshot snapshot, InstallationLimits limits, DateTimeOffset now) =>
        StepDeadlines.For(now, ValidationRun.CommandLimit(snapshot.Configuration.Limits, limits), StepDeadlines.TaskLimit(snapshot.Configuration.Limits, limits), task.TimeInRunning(now));

    // A command of Validation that passed: the next one runs, or, when all have, the pull request is journaled for publication. A command that did not pass ended its step run with a failure, which AfterFailureAsync decides on.

    private async Task<bool> AfterValidationAsync(TaskRecord task, TaskSnapshot snapshot, IReadOnlyList<StepRun> steps, EffectsRun run, CancellationToken cancellationToken)
    {
        var anchor = steps.Last(step => !step.IsValidation);
        var validation = ValidationRun.Begin(snapshot.Configuration.Validation);

        // A command that was attempted again has more than one step run, and the last attempt is the one that counts.
        var attempts = steps.Where(step => step.IsValidation && step.Sequence > anchor.Sequence).GroupBy(step => step.CommandIndex!.Value).OrderBy(group => group.Key);
        foreach (var step in attempts.Select(group => group.OrderBy(step => step.Sequence).Last()))
        {
            var index = step.CommandIndex!.Value;
            validation = validation.Record(ValidationCommandResult.Ended(index, snapshot.Configuration.Validation[index], step.Id, step.ExitCode, step.Cause ?? StepExitCause.Lost, step.Duration ?? TimeSpan.Zero));
        }

        var tip = steps[^1].TipBefore;
        if (!validation.IsDecided)
        {
            return await PlanValidationAsync(task, snapshot, anchor.Round, tip, validation.NextIndex!.Value, cancellationToken);
        }

        if (validation.Outcome != ValidationOutcome.Passed)
        {
            return false;
        }

        if (ImplementationResultOf(anchor) is not { } result)
        {
            return await FailStepAsync(task, snapshot, steps[^1], FailureClass.InfrastructureError, "the result of the Implementation cannot be read", run, cancellationToken);
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

    // A step that failed (AD-11, NFR-10): the work an Agent's step left is kept first, then the step is attempted again as a new step run while its class spends the step retry bound and the Task has time left, and otherwise the Task fails with a
    // comment on its issue. Every decision is made from the step run, the journal, and the Task's time, so a tick can stop between any two of them.

    /// <summary>What bounds a Task's steps now: how many times a step is attempted again, the Task duration limit, and how much of it is left.</summary>
    private sealed record Bounds(int Retries, TimeSpan TaskLimit, TimeSpan Remaining, InstallationLimits Limits);

    private async Task<Bounds> ReadBoundsAsync(TaskRecord task, TaskSnapshot snapshot, CancellationToken cancellationToken)
    {
        var limits = await reads.GetLimitsAsync(cancellationToken);
        var taskLimit = StepDeadlines.TaskLimit(snapshot.Configuration.Limits, limits);
        var retries = InstallationSettings.StepRetriesFrom(await reads.ListSettingsAsync(cancellationToken));
        return new Bounds(retries, taskLimit, StepDeadlines.Remaining(taskLimit, task.TimeInRunning(time.GetUtcNow())), limits);
    }

    /// <summary>
    /// Carries on a step run that ended, or whose work was kept, with a failure. The work of an Agent step whose container ran is checkpointed and pushed before anything else (FR-20), so that a step attempted again starts from it and the Task branch holds all work
    /// done; the decision follows once the push is made. Returns whether the records changed in a way that lets the Task go further at once.
    /// </summary>
    private async Task<bool> AfterFailureAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, EffectsRun run, CancellationToken cancellationToken)
    {
        if (!FailureClasses.TryParse(step.Failure, out var failure))
        {
            failure = FailureClass.Unknown;
        }

        if (step.Status == StepRunStatus.Ended && !step.IsValidation && step.StartedAt is not null && await KeepWorkAsync(task, snapshot, step, run, cancellationToken) is { } progress)
        {
            return progress;
        }

        // A step that kept work waits for its push, which may also end in an Escalation that takes the Task out of Running.
        if (!await IsPushedAsync(task, step, cancellationToken))
        {
            return false;
        }

        return await ConcludeFailureAsync(task, snapshot, step, failure, run, cancellationToken);
    }

    /// <summary>
    /// Keeps what a failed Agent step left in the Task's volume as one commit, whose push is journaled with it. Returns null when there is nothing to wait for: the work was left behind because it could not be kept, which is said in the log, or because GitHub is not connected; otherwise whether the step
    /// moved on, false when the attempt is to be made again by the next tick, up to <see cref="MaximumStartAttempts"/> times.
    /// </summary>
    private async Task<bool?> KeepWorkAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, EffectsRun run, CancellationToken cancellationToken)
    {
        string failed;
        try
        {
            if (await reads.GetGitHubAppSlugAsync(cancellationToken) is not { } slug)
            {
                run.TaskProblems.Add($"Task {task.Id} of issue #{task.IssueNumber}: GitHub is not connected, so the work of the {step.Kind.Tag()} step cannot be committed and is left behind.");
                return null;
            }

            if (await BundleOfAsync(task, step, step.TipBefore, cancellationToken) is not { } parent)
            {
                failed = "the commit the work was started from could not be bundled";
            }
            else
            {
                var checkpoint = await CheckpointAsync(task, snapshot, step, slug, parent, cancellationToken);
                _startFailures.Remove(step.Id);
                return checkpoint.Bundle is not { } bundle
                    ? await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Checkpointed, TipAfter: step.TipBefore), cancellationToken)
                    : await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Checkpointed, TipAfter: bundle.Tip, Effects: [PushIntent.For(step.Id, bundle, snapshot.CommitId).ToNewEffect(task.Id)]), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failed = $"{exception.GetType().Name}: {exception.Message}";
        }

        if (CountStartFailure(step) < MaximumStartAttempts)
        {
            return false;
        }

        _startFailures.Remove(step.Id);
        run.TaskProblems.Add($"Task {task.Id} of issue #{task.IssueNumber}: the work of the {step.Kind.Tag()} step could not be kept and is left behind: {failed}.");
        return null;
    }

    /// <summary>
    /// The step run <paramref name="step"/> failed with <paramref name="failure"/>, and its work is kept as far as it can be. In one transaction it is concluded and either attempted again, as a new step run of the same step with the next attempt, on the last pushed tip,
    /// or the Task fails, with its label and a comment on the issue.
    /// </summary>
    private async Task<bool> ConcludeFailureAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, FailureClass failure, EffectsRun run, CancellationToken cancellationToken)
    {
        var bounds = await ReadBoundsAsync(task, snapshot, cancellationToken);
        var end = StepRetries.Decide(failure, step.Attempt, bounds.Retries, bounds.Remaining);
        if (end != StepFailureEnd.Retry)
        {
            await EndTaskAsync(task, step, failure, FailureFactsOf(task, snapshot, step, failure, end, bounds), run, cancellationToken);
            return false;
        }

        var now = time.GetUtcNow();
        var stepLimit = step.IsValidation ? ValidationRun.CommandLimit(snapshot.Configuration.Limits, bounds.Limits) : bounds.TaskLimit;
        var retry = new NewStepRun(
            EntityId.New(time),
            task.Id,
            step.Kind,
            step.Round,
            StepDeadlines.For(now, stepLimit, bounds.TaskLimit, task.TimeInRunning(now)),
            now,
            await LastPushedTipAsync(task, snapshot, cancellationToken),
            step.Agent,
            step.Model,
            step.Effort,
            step.Methodology,
            step.Template,
            step.CommandIndex,
            step.Attempt + 1);
        if (!await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Concluded, Failure: failure, Retry: retry), cancellationToken))
        {
            return false;
        }

        run.TaskProblems.Add($"Task {task.Id} of issue #{task.IssueNumber}: {step.Kind.Tag()} attempt {step.Attempt} failed ({failure.Tag()}) and is attempted again.");
        return true;
    }

    /// <summary>What the comment on the issue of a Task that failed says: the class, the step, the Validation command and its exit status, the attempts and the bound, the limit when it ended the Task, and the branch.</summary>
    private static TaskFailureFacts FailureFactsOf(TaskRecord task, TaskSnapshot snapshot, StepRun step, FailureClass failure, StepFailureEnd end, Bounds bounds)
    {
        var command = step.IsValidation && step.CommandIndex is { } index && index < snapshot.Configuration.Validation.Count ? snapshot.Configuration.Validation[index] : null;
        return new TaskFailureFacts(
            failure.Tag(),
            step.Kind.Tag(),
            command,
            command is null ? null : step.ExitCode,
            step.Attempt,
            bounds.Retries,
            end.Tag(),
            end == StepFailureEnd.DurationLimit ? LimitFormat.FormatDuration(bounds.TaskLimit) : null,
            task.Branch);
    }

    /// <summary>Ends the Task as <c>Failed</c> with the intents of its label and of the comment that says what was attempted, which class ended it, and which command or step failed; its branch keeps all the work done.</summary>
    private async Task<bool> EndTaskAsync(TaskRecord task, StepRun step, FailureClass failure, TaskFailureFacts facts, EffectsRun run, CancellationToken cancellationToken)
    {
        var cause = EffectKeys.StepCause(step.Id);
        var move = new TaskMove(
            TaskState.Running,
            TaskState.Failed,
            [new NewEffect(task.Id, cause, EffectKind.Label, TaskState.Failed.Tag()), TaskFailureNotice.ToNewEffect(task.Id, cause, task.RepositoryId, task.IssueNumber, facts)]);
        if (!await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Concluded, Move: move, Failure: failure), cancellationToken))
        {
            return false;
        }

        run.Moved[task.Id] = TaskState.Failed;
        run.TaskProblems.Add($"Task {task.Id} of issue #{task.IssueNumber} failed in {step.Kind.Tag()} ({failure.Tag()}) after {step.Attempt} attempt(s): {facts.End}.");
        return true;
    }

    /// <summary>
    /// A step that could not be carried out. A step that never ended ends with the failure and is decided on by the next step of the Task; one that ended well and could not be carried on is concluded with it at once. Returns whether the records changed
    /// in a way that lets the Task go further.
    /// </summary>
    private async Task<bool> FailStepAsync(TaskRecord task, TaskSnapshot snapshot, StepRun step, FailureClass failure, string detail, EffectsRun run, CancellationToken cancellationToken)
    {
        run.TaskProblems.Add($"Task {task.Id} of issue #{task.IssueNumber}: {step.Kind.Tag()} attempt {step.Attempt} could not be carried out ({failure.Tag()}): {detail}.");
        if (step.Status is StepRunStatus.Planned or StepRunStatus.Started)
        {
            return await work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, new StepRunEnd(time.GetUtcNow(), null, StepExitCause.Lost, failure, null, null)), cancellationToken);
        }

        return await ConcludeFailureAsync(task, snapshot, step, failure, run, cancellationToken);
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

        // A step that failed is recorded as it failed and nothing more: whether it is attempted again or ends the Task is decided from that record by the Task's next step (AD-11).
        var at = time.GetUtcNow();
        var end = step.IsValidation
            ? await EndOfValidationAsync(step, signal, cause, at, cancellationToken)
            : await EndOfAgentStepAsync(step, signal, cause, at, cancellationToken);
        return applied with { StepRun = new StepRunChange(step.Id, StepRunStatus.Ended, end) };
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

        // The runtime's cause comes first: a container it stopped or lost has no stream that is read (AD-11).
        if (FailureClassifier.FromRuntime(cause) is { } runtimeCause)
        {
            return Ended(runtimeCause);
        }

        var adapter = _adapters.FirstOrDefault(candidate => candidate.Agent == step.Agent);
        if (adapter is null)
        {
            return Ended(FailureClass.Unknown);
        }

        AgentOutput parsed;
        try
        {
            await using var stream = await output!.OpenReadAsync(step.Id, cancellationToken);
            parsed = stream is null ? new AgentOutput([], null, [], 0) : adapter.Parse(ReadLines(stream));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or FormatException)
        {
            // What was stored cannot be read, or is not what the adapter's CLI prints: the step has no result, and a Signal that failed to apply again and again would leave the Task holding the slot.
            return Ended(FailureClass.AgentError);
        }

        // Then the adapter's reading of a CLI that ended by itself, then Core's reading of the result, which is validated by strict deserialization and never interpreted when it is missing or invalid.
        var read = StepOutcomes.Read(parsed.Result?.StructuredOutput);
        return FailureClassifier.Decide(cause, () => adapter.ClassifyExit(signal.ExitCode ?? -1, parsed), () => read is not null) is { } failure
            ? Ended(failure)
            : Ended(null, read!.Outcome == StepOutcome.Completed ? StepOutcomes.Completed : StepOutcomes.NeedsDecision, parsed.Result!.StructuredOutput);
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
