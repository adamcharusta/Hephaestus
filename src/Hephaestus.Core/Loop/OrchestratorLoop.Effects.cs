using Hephaestus.Core.Effects;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Settings;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Loop;

/// <summary>
/// The loop's second phase (AD-5): the effects on GitHub that applying Signals journaled. An intent is committed with the state change that
/// causes it and the effect is made after that commit; its result, or the time of its next attempt, is its own transaction. The phase
/// belongs to the loop, because the journal has one writer.
/// </summary>
public sealed partial class OrchestratorLoop
{
    /// <summary>How many effects one tick attempts at most, so that the loop beats and the Signals behind are not held back by a slow GitHub.</summary>
    public const int MaximumEffectsPerTick = 20;

    /// <summary>How long to wait when GitHub is rate-limited and says no time.</summary>
    private static readonly TimeSpan RateLimitWait = TimeSpan.FromMinutes(1);

    private sealed class EffectsRun
    {
        public int Ended { get; set; }

        public int Retried { get; set; }

        public List<EffectProblem> Problems { get; } = [];

        /// <summary>The state each Task that an effect moved in this tick has now.</summary>
        public Dictionary<Guid, TaskState> Moved { get; } = [];

        public Exception? Failure { get; set; }
    }

    /// <summary>
    /// Makes every effect that has an intent and no result and is due: those journaled in this tick, and, in the first tick, those that were
    /// journaled before the host stopped, which may or may not have been made. Every attempt starts by reading the far side, so a change
    /// that was made and not recorded is found and recorded, not made again (NFR-9).
    /// </summary>
    private async Task RunEffectsAsync(EffectsRun run, CancellationToken cancellationToken)
    {
        var unfinished = await reads.ListUnfinishedEffectsAsync(cancellationToken);
        var due = unfinished.Where(pending => pending.NextAttemptAt is not { } at || at <= time.GetUtcNow()).Take(MaximumEffectsPerTick).ToList();
        if (due.Count == 0)
        {
            return;
        }

        var retries = InstallationSettings.EffectRetriesFrom(await reads.ListSettingsAsync(cancellationToken));
        foreach (var listed in due)
        {
            // An earlier effect of this tick may have moved the Task, and what is decided about it now is decided about the Task as it is.
            var pending = run.Moved.TryGetValue(listed.Task.Id, out var state) ? listed with { Task = listed.Task with { State = state } } : listed;
            // When GitHub as a whole is not answering, the rest would only fail the same way and spend their retries for nothing.
            if (!await AttemptAsync(pending, retries, run, cancellationToken))
            {
                break;
            }

            heartbeat.Beat();
        }
    }

    /// <summary>Attempts one effect. Returns false when GitHub as a whole is not available, so that the tick stops attempting.</summary>
    private async Task<bool> AttemptAsync(PendingEffect pending, int retries, EffectsRun run, CancellationToken cancellationToken)
    {
        var (effect, task, failures, _) = pending;
        if (task.State.Tag() != effect.Detail)
        {
            // A later effect, journaled with the change of state, shows the state the Task has now.
            await EndAsync(effect, EffectResult.Superseded, null, failures, run, cancellationToken);
            return true;
        }

        var read = await CallAsync(() => host.ReadIssueLabelsAsync(task.RepositoryId, task.IssueNumber, cancellationToken), run);
        if (read.Failure != GitHubIssueFailure.None)
        {
            return await FailedAsync(pending, read, retries, run, cancellationToken);
        }

        var (add, remove) = Labels.PlanState(read.Labels, task.State);
        if (add.Count == 0 && remove.Count == 0)
        {
            await EndAsync(effect, EffectResult.AlreadyInPlace, null, failures, run, cancellationToken);
            return true;
        }

        var change = await CallAsync(() => host.ChangeIssueLabelsAsync(task.RepositoryId, task.IssueNumber, add, remove, effect.Key, cancellationToken), run);
        if (change.Failure != GitHubIssueFailure.None)
        {
            return await FailedAsync(pending, change, retries, run, cancellationToken);
        }

        await EndAsync(effect, EffectResult.Applied, null, failures, run, cancellationToken);
        return true;
    }

    /// <summary>A gateway that throws is treated as one that did not answer: the attempt counts, and the exception is reported with the tick.</summary>
    private static async Task<GitHubIssueResult> CallAsync(Func<Task<GitHubIssueResult>> call, EffectsRun run)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Failure ??= exception;
            return GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        }
    }

    private async Task<bool> FailedAsync(PendingEffect pending, GitHubIssueResult result, int retries, EffectsRun run, CancellationToken cancellationToken)
    {
        var (effect, task, failures, _) = pending;
        var now = time.GetUtcNow();
        switch (result.Failure)
        {
            case GitHubIssueFailure.RateLimited:
                // GitHub asked to wait: the wait is not a failure, and nothing else is sent until it is over.
                await RetryAsync(effect, EffectReasons.RateLimited, failures, result.WaitUntil ?? now + RateLimitWait, run, cancellationToken);
                return false;

            case GitHubIssueFailure.Unreachable or GitHubIssueFailure.NotConnected or GitHubIssueFailure.KeyUnreadable:
                var reason = result.Failure switch
                {
                    GitHubIssueFailure.NotConnected => EffectReasons.NotConnected,
                    GitHubIssueFailure.KeyUnreadable => EffectReasons.KeyUnreadable,
                    _ => EffectReasons.Unreachable,
                };

                failures++;
                if (failures > retries)
                {
                    await ConcludeAsync(pending, EffectResult.GaveUp, reason, failures, run, cancellationToken);
                }
                else
                {
                    await RetryAsync(effect, reason, failures, now + EffectRetries.DelayAfter(failures), run, cancellationToken);
                }

                return false;

            default:
                // Refused for good: another attempt would be refused again.
                await ConcludeAsync(
                    pending,
                    EffectResult.Refused,
                    result.Failure switch
                    {
                        GitHubIssueFailure.IssueNotFound => EffectReasons.IssueNotFound,
                        GitHubIssueFailure.NotInstalled => EffectReasons.AppNotInstalled,
                        _ => EffectReasons.Rejected,
                    },
                    failures,
                    run,
                    cancellationToken);
                return true;
        }
    }

    private async Task EndAsync(Effect effect, EffectResult result, string? reason, int failures, EffectsRun run, CancellationToken cancellationToken)
    {
        if (await work.RecordEffectAsync(new EffectRecording(effect, result, reason, failures, NextAttemptAt: null), cancellationToken))
        {
            run.Ended++;
        }
    }

    private async Task RetryAsync(Effect effect, string reason, int failures, DateTimeOffset next, EffectsRun run, CancellationToken cancellationToken)
    {
        if (await work.RecordEffectAsync(new EffectRecording(effect, Result: null, reason, failures, next), cancellationToken))
        {
            run.Retried++;
        }
    }

    /// <summary>
    /// Ends an effect that will not be made: the refusal or the give-up is journaled with its reason, and the Task moves to
    /// <c>waiting-for-owner</c>, with the intent of the label that shows it, in the same transaction (AD-5). A Task that is not in a state
    /// that moves stays as it is; it is the log that tells the Owner, and an error with the Task's id is logged either way.
    /// </summary>
    private async Task ConcludeAsync(PendingEffect pending, EffectResult result, string reason, int failures, EffectsRun run, CancellationToken cancellationToken)
    {
        var (effect, task, _, _) = pending;
        var moves = task.State.MovesToWaitingForOwnerWhenAnEffectFails();
        var recording = new EffectRecording(
            effect,
            result,
            reason,
            failures,
            NextAttemptAt: null,
            moves ? TaskState.WaitingForOwner : null,
            moves ? [new NewEffect(task.Id, effect.Cause, EffectKind.Label, TaskState.WaitingForOwner.Tag())] : null);
        if (await work.RecordEffectAsync(recording, cancellationToken))
        {
            run.Ended++;
            run.Problems.Add(new EffectProblem(task.Id, effect.Key, result, reason, moves));
            if (moves)
            {
                run.Moved[task.Id] = TaskState.WaitingForOwner;
            }
        }
    }
}
