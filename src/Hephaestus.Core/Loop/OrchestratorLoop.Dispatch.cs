using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Methodologies;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Windows;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Loop;

/// <summary>
/// The loop's third phase (AD-1, FR-14, FR-16): the Tasks. The one that is <c>Running</c> is carried a step further; when none is, the Task the dispatch policy puts first is started, with the Project
/// Configuration it will use snapshotted onto it; and a window with no Task left to work is closed. Nothing here waits for a step (AD-18): a step is recorded, its container is asked for, and the
/// runtime's step-exited Signal ends it, so a tick can stop anywhere and the next one decides again from what is recorded.
/// </summary>
public sealed partial class OrchestratorLoop
{
    /// <summary>How long a Task that could not be started is left alone, so that a Project whose file is wrong is not read from GitHub on every tick.</summary>
    public static readonly TimeSpan StartRetryDelay = TimeSpan.FromMinutes(1);

    private readonly Dictionary<Guid, DateTimeOffset> _startNotBefore = [];
    private readonly IReadOnlyList<IAgentAdapter> _adapters = [.. adapters ?? []];
    private readonly ProjectConfigurationReader? _configurations = methodologies is null ? null : new ProjectConfigurationReader(host, reads, methodologies);

    private bool CanRunTasks => runtime is not null && mirrors is not null && methodologies is not null && output is not null && _configurations is not null;

    private async Task RunTasksAsync(EffectsRun run, CancellationToken cancellationToken)
    {
        if (CanRunTasks)
        {
            var running = await reads.ListTasksAsync(TaskState.Running, cancellationToken);
            foreach (var task in running)
            {
                try
                {
                    await AdvanceAsync(task, run, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A runtime that cannot be reached, or a store that fails, is the tick's failure and is reported with it; the Task is carried on again by the next tick, and the rest of this one still runs.
                    run.Failure ??= exception;
                }
            }

            // Only one Task runs (FR-14): the next starts when that one has left Running, which a step that just ended may have made so.
            if (running.Count == 0 || (await reads.ListTasksAsync(TaskState.Running, cancellationToken)).Count == 0)
            {
                try
                {
                    await DispatchNextAsync(run, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    run.Failure ??= exception;
                }
            }
        }

        await CloseDrainedWindowsAsync(cancellationToken);
    }

    /// <summary>Starts the first Task of the open windows that can be started. A Task that cannot be, because its Project's file is wrong or its Agent is not ready, is passed over, and the Tasks of other Projects are not held back by it.</summary>
    private async Task DispatchNextAsync(EffectsRun run, CancellationToken cancellationToken)
    {
        var windows = await reads.ListWindowsAsync(openOnly: true, cancellationToken);
        if (windows.Count == 0)
        {
            return;
        }

        var queued = await reads.ListTasksAsync(TaskState.Queued, cancellationToken);
        var now = time.GetUtcNow();
        foreach (var gone in _startNotBefore.Keys.Where(id => queued.All(task => task.Id != id)).ToList())
        {
            _startNotBefore.Remove(gone);
        }

        foreach (var task in DispatchPolicy.InWindows(queued, windows.Select(window => window.RepositoryId).ToHashSet()))
        {
            if (_startNotBefore.TryGetValue(task.Id, out var notBefore) && notBefore > now)
            {
                continue;
            }

            (bool Started, string? Problem) attempt;
            try
            {
                attempt = await TryStartAsync(task, run, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Whatever one Task's start meets, a runtime that cannot be asked among it, the Tasks of other Projects are still tried.
                run.Failure ??= exception;
                attempt = (false, "its start met a failure that the log of the tick holds");
            }

            var (started, problem) = attempt;
            if (started)
            {
                _startNotBefore.Remove(task.Id);
                run.Started++;
                await AdvanceAsync(task, run, cancellationToken);
                return;
            }

            if (problem is not null)
            {
                _startNotBefore[task.Id] = now + StartRetryDelay;
                run.TaskProblems.Add($"Task {task.Id} of issue #{task.IssueNumber} was not started: {problem}.");
            }
        }
    }

    /// <summary>
    /// Starts one Task (AD-1, AD-16). The Project Configuration is read from the Base Branch once, validated, and snapshotted with the commit it was read at and the image it resolved to, with the digest; the Task then
    /// moves to <c>Running</c> with its first step planned, in one transaction. A Task that has a snapshot from an earlier start keeps it. Returns whether the Task started, and, when it did not, what was wrong, or null when
    /// it was no longer there to start.
    /// </summary>
    private async Task<(bool Started, string? Problem)> TryStartAsync(TaskRecord task, EffectsRun run, CancellationToken cancellationToken)
    {
        var project = (await reads.ListProjectsAsync(includeRemoved: false, cancellationToken)).FirstOrDefault(candidate => candidate.RepositoryId == task.RepositoryId);
        if (project is null)
        {
            return (false, "its Project is not registered");
        }

        var snapshot = await reads.FindTaskSnapshotAsync(task.Id, cancellationToken);
        TaskSnapshot? created = null;
        if (snapshot is null)
        {
            ConfigurationCheck check;
            try
            {
                check = await _configurations!.ReadAsync(project, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                run.Failure ??= exception;
                return (false, "its configuration could not be read from GitHub");
            }

            if (check is not { Failure: ProjectFailure.None, Status: ConfigurationStatus.Valid, Configuration: { } configuration, CommitId: { } commitId })
            {
                return (false, check.Failure != ProjectFailure.None ? $"its configuration could not be read ({check.Failure})" : $"its configuration is {check.Status.ToString()!.ToLowerInvariant()}");
            }

            ResolvedImage image;
            try
            {
                image = await runtime!.ResolveImageAsync(configuration.Image, cancellationToken);
            }
            catch (ImageNotUsableException unusable)
            {
                return (false, $"its image cannot be used ({unusable.Info.Id})");
            }

            snapshot = created = new TaskSnapshot(task.Id, commitId, configuration, image, time.GetUtcNow());
        }

        var (choice, why) = await ChooseAgentAsync(snapshot.Configuration, cancellationToken);
        if (choice is null)
        {
            return (false, why);
        }

        var methodology = await methodologies!.ReadAsync(snapshot.Configuration.Methodology, cancellationToken);
        if (methodology is null || MethodologyTemplates.Select(methodology, StepKind.Implementation) is not { } template)
        {
            return (false, $"its Methodology '{snapshot.Configuration.Methodology}' has no template for an implementation");
        }

        var now = time.GetUtcNow();
        var limits = await reads.GetLimitsAsync(cancellationToken);
        var step = new NewStepRun(
            EntityId.New(time),
            task.Id,
            StepKind.Implementation,
            Round: 0,
            StepDeadlines.For(now, StepDeadlines.AgentLimit(snapshot.Configuration.Limits, limits), StepDeadlines.TaskLimit(snapshot.Configuration.Limits, limits), task.TimeInRunning(now)),
            now,
            await LastPushedTipAsync(task, snapshot, cancellationToken),
            choice.Agent,
            choice.Model,
            choice.Effort,
            snapshot.Configuration.Methodology,
            template.Identity.ToString());
        var dispatch = new TaskDispatch(task.Id, created, step, [new NewEffect(task.Id, EffectKeys.StepCause(step.Id), EffectKind.Label, TaskState.Running.Tag())]);
        if (!await work.DispatchAsync(dispatch, cancellationToken))
        {
            return (false, null);
        }

        run.Moved[task.Id] = TaskState.Running;
        return (true, null);
    }

    private sealed record AgentChoice(Agent Agent, string Model, Effort? Effort, AuthenticationMode Mode, IAgentAdapter Adapter);

    /// <summary>
    /// The Primary Agent of a Task, from the snapshot: the Alias the Project names resolves to an Agent and a Model, the Effort Alias to an Effort, and the Agent signs in as the Owner chose (FR-5, FR-24). An Agent that has no
    /// adapter, does not support the Effort or the sign-in mode, or is known to have no capacity (AD-20) is not chosen: the Task is not started, and what is wrong is said in the log; Story 3.15 says it on the issue.
    /// </summary>
    private async Task<(AgentChoice? Choice, string? Problem)> ChooseAgentAsync(ProjectConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.PrimaryAlias is not { } primaryName)
        {
            return (null, "its Project names no primary Alias");
        }

        var aliases = await reads.ListAliasesAsync(cancellationToken);
        if (aliases.FirstOrDefault(alias => alias.Name == primaryName) is not { Agent: { } agent, Model: { } model })
        {
            return (null, $"the Alias '{primaryName}' is no longer one that names an Agent");
        }

        Effort? effort = null;
        if (configuration.Effort is { } effortName)
        {
            if (aliases.FirstOrDefault(alias => alias.Name == effortName)?.Effort is not { } resolved)
            {
                return (null, $"the Alias '{effortName}' is no longer one that names an Effort");
            }

            effort = resolved;
        }

        var adapter = _adapters.FirstOrDefault(candidate => candidate.Agent == agent);
        if (adapter is null)
        {
            return (null, $"this release has no adapter for {agent.Tag()}");
        }

        if (effort is { } asked && !adapter.SupportsEffort(asked).IsSupported)
        {
            return (null, $"{agent.Tag()} does not support the effort {asked.Tag()}");
        }

        var setting = (await reads.ListAgentSettingsAsync(cancellationToken)).FirstOrDefault(candidate => candidate.Agent == agent);
        if (setting is null)
        {
            return (null, $"nobody has chosen how {agent.Tag()} signs in");
        }

        if (!adapter.SupportsAuthentication(setting.Mode).IsSupported)
        {
            return (null, $"{agent.Tag()} cannot be driven with {setting.Mode.Tag()} sign-in");
        }

        var capacity = (await reads.ListAgentCapacitiesAsync(cancellationToken)).FirstOrDefault(candidate => candidate.Agent == agent && candidate.Mode == setting.Mode);
        if (capacity is not null && !capacity.HasCapacity(time.GetUtcNow()))
        {
            return (null, $"{agent.Tag()} has no capacity");
        }

        return (new AgentChoice(agent, model, effort, setting.Mode, adapter), null);
    }

    /// <summary>The tip the next step's checkout is made from: the tip of the last push the journal shows made, or, when nothing was pushed, the commit of the snapshot (AD-9).</summary>
    private async Task<string> LastPushedTipAsync(TaskRecord task, TaskSnapshot snapshot, CancellationToken cancellationToken)
    {
        var journal = await reads.ListJournalAsync(task.Id, cancellationToken);
        var pushed = journal.LastOrDefault(entry => entry.Entry == JournalEntryKind.Result && entry.Effect.Kind == EffectKind.Push && entry.Result is EffectResult.Applied or EffectResult.AlreadyInPlace);
        return pushed is not null && PushIntent.TryParse(pushed.Effect.Detail, out var intent) ? intent.Tip : snapshot.CommitId;
    }

    /// <summary>A window ends when its Project has no Task left that could be dispatched (FR-16); a Task that is <c>Running</c> finishes all the same.</summary>
    private async Task CloseDrainedWindowsAsync(CancellationToken cancellationToken)
    {
        var windows = await reads.ListWindowsAsync(openOnly: true, cancellationToken);
        if (windows.Count == 0)
        {
            return;
        }

        foreach (var window in DispatchPolicy.Drained(await reads.ListTasksAsync(TaskState.Queued, cancellationToken), windows))
        {
            await work.CloseWindowAsync(new WindowClosing(window.Id, WindowEnd.Drained), cancellationToken);
        }

        // A Project the Owner removed has nothing more to be worked, whatever waits in its Queue (FR-1): its window is closed as the Owner's removal would have it.
        var registered = (await reads.ListProjectsAsync(includeRemoved: false, cancellationToken)).Select(project => project.RepositoryId).ToHashSet();
        foreach (var window in windows.Where(window => !registered.Contains(window.RepositoryId)))
        {
            await work.CloseWindowAsync(new WindowClosing(window.Id, WindowEnd.Stopped), cancellationToken);
        }
    }
}
