using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Failures;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.PullRequests;
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

        /// <summary>How many Tasks this tick moved from <c>Queued</c> to <c>Running</c>.</summary>
        public int Started { get; set; }

        /// <summary>The Tasks this tick could not start, one line each.</summary>
        public List<string> TaskProblems { get; } = [];
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

        var settings = await reads.ListSettingsAsync(cancellationToken);
        var retries = InstallationSettings.EffectRetriesFrom(settings);
        // No Task or Project has a language of its own yet (AD-21), so the text on GitHub is in the installation's.
        var language = LanguageResolver.ForText(task: null, project: null, LanguageResolver.Installation(InstallationSettings.StoredLanguage(settings), fallbackLanguage));
        foreach (var listed in due)
        {
            // An earlier effect of this tick may have moved the Task, and what is decided about it now is decided about the Task as it is.
            var pending = listed.Task is { } task && run.Moved.TryGetValue(task.Id, out var state) ? listed with { Task = task with { State = state } } : listed;
            // When GitHub as a whole is not answering, the rest would only fail the same way and spend their retries for nothing.
            if (!await AttemptAsync(pending, retries, language, run, cancellationToken))
            {
                break;
            }

            heartbeat.Beat();
        }
    }

    /// <summary>Attempts one effect. Returns false when GitHub as a whole is not available, so that the tick stops attempting.</summary>
    private Task<bool> AttemptAsync(PendingEffect pending, int retries, Language language, EffectsRun run, CancellationToken cancellationToken) =>
        pending.Effect.Kind switch
        {
            EffectKind.Comment => AttemptCommentAsync(pending, retries, language, run, cancellationToken),
            EffectKind.Notification => AttemptNotificationAsync(pending, run, cancellationToken),
            EffectKind.Push => AttemptPushAsync(pending, retries, run, cancellationToken),
            EffectKind.PullRequest => AttemptPullRequestAsync(pending, retries, language, run, cancellationToken),
            _ => AttemptLabelAsync(pending, retries, run, cancellationToken),
        };

    /// <summary>
    /// Raises an Escalation for a Task (FR-48): the one path by which Hephaestus asks the Owner something. The Escalation, the Task's move to
    /// <c>waiting-for-owner</c>, and the intents of its label, its comment, and its notification commit together (AD-1, AD-5); they are made after
    /// that commit, by this or the next tick. Returns false, and writes nothing, when the Task is in no state that moves, which includes a Task that
    /// waits already, so that one Escalation asks for one decision. Other Tasks are not held back by it (see <see cref="Tasks.DispatchPolicy"/>).
    /// </summary>
    /// <param name="cause">What caused it, as the journal names causes, for example the step run that returned the question.</param>
    public async Task<bool> RaiseEscalationAsync(TaskRecord task, EscalationRequest request, string cause, CancellationToken cancellationToken) =>
        EscalationPath.Raise(task, request, cause, time) is { } plan && await work.EscalateAsync(plan, cancellationToken);

    private async Task<bool> AttemptLabelAsync(PendingEffect pending, int retries, EffectsRun run, CancellationToken cancellationToken)
    {
        var (effect, found, failures, _) = pending;
        if (found is not { } task)
        {
            // A label shows a Task's state, so one that is for no Task cannot be made, now or later.
            await ConcludeAsync(pending, EffectResult.Refused, EffectReasons.UnknownEffect, failures, run, cancellationToken);
            return true;
        }

        if (task.State.Tag() != effect.Detail)
        {
            // A later effect, journaled with the change of state, shows the state the Task has now.
            await EndAsync(effect, EffectResult.Superseded, null, failures, run, cancellationToken);
            return true;
        }

        var read = await CallAsync(() => host.ReadIssueLabelsAsync(task.RepositoryId, task.IssueNumber, cancellationToken), GitHubIssueResult.Failed, run);
        if (read.Failure != GitHubIssueFailure.None)
        {
            return await FailedAsync(pending, read.Failure, read.WaitUntil, retries, run, cancellationToken);
        }

        var (add, remove) = Labels.PlanState(read.Labels, task.State);
        if (add.Count == 0 && remove.Count == 0)
        {
            await EndAsync(effect, EffectResult.AlreadyInPlace, null, failures, run, cancellationToken);
            return true;
        }

        var change = await CallAsync(() => host.ChangeIssueLabelsAsync(task.RepositoryId, task.IssueNumber, add, remove, effect.Key, cancellationToken), GitHubIssueResult.Failed, run);
        if (change.Failure != GitHubIssueFailure.None)
        {
            return await FailedAsync(pending, change.Failure, change.WaitUntil, retries, run, cancellationToken);
        }

        await EndAsync(effect, EffectResult.Applied, null, failures, run, cancellationToken);
        return true;
    }

    /// <summary>
    /// Says on an issue what Hephaestus ignored (Story 2.5). Like a label, it starts by reading the far side: the comment is looked for by its
    /// marker, so that one posted before a crash and not recorded is found and not posted again (NFR-9).
    /// </summary>
    private async Task<bool> AttemptCommentAsync(PendingEffect pending, int retries, Language language, EffectsRun run, CancellationToken cancellationToken)
    {
        var (effect, _, failures, _) = pending;
        var text = await CommentTextAsync(effect, language, cancellationToken);
        if (effect.RepositoryId is not { } repositoryId || effect.IssueNumber is not { } issueNumber || text is null)
        {
            // An intent that this version cannot make: it is never going to be, so it ends as a refusal and not as a loop that stops here.
            await ConcludeAsync(pending, EffectResult.Refused, EffectReasons.UnknownEffect, failures, run, cancellationToken);
            return true;
        }

        var found = await CallAsync(() => host.FindIssueCommentAsync(repositoryId, issueNumber, effect.Key, cancellationToken), GitHubCommentResult.Failed, run);
        if (found.Failure != GitHubIssueFailure.None)
        {
            return await FailedAsync(pending, found.Failure, found.WaitUntil, retries, run, cancellationToken);
        }

        if (found.CommentId is not null)
        {
            await EndAsync(effect, EffectResult.AlreadyInPlace, null, failures, run, cancellationToken);
            return true;
        }

        var posted = await CallAsync(() => host.PostIssueCommentAsync(repositoryId, issueNumber, text, effect.Key, cancellationToken), GitHubCommentResult.Failed, run);
        if (posted.Failure != GitHubIssueFailure.None)
        {
            return await FailedAsync(pending, posted.Failure, posted.WaitUntil, retries, run, cancellationToken);
        }

        await EndAsync(effect, EffectResult.Applied, null, failures, run, cancellationToken);
        return true;
    }

    /// <summary>The sentence of a comment: an Escalation's, a failed Task's, or what Hephaestus ignored; null when the intent is of no kind this version knows.</summary>
    private async Task<string?> CommentTextAsync(Effect effect, Language language, CancellationToken cancellationToken)
    {
        if (EscalationPath.TryParseDetail(effect.Detail, out var escalationId))
        {
            return await reads.FindEscalationAsync(escalationId, cancellationToken) is { } escalation ? EscalationComment.Compose(escalation, language) : null;
        }

        if (TaskFailureNotice.TryWrite(effect, language, out var failure))
        {
            return failure;
        }

        return IssueNotices.TryWrite(effect.Detail, language, out var text) ? text : null;
    }

    /// <summary>
    /// Tells the Owner, off GitHub, that an Escalation was raised (AR-63). A notification is made at most once (AD-5): that it is being made is
    /// journaled before the call, so a host that stopped in between does not send it again, and ends it as given up. Whatever the notifier does,
    /// including nothing or failing, no Task changes (FR-51), and a failure is logged with the effect's key.
    /// </summary>
    private async Task<bool> AttemptNotificationAsync(PendingEffect pending, EffectsRun run, CancellationToken cancellationToken)
    {
        var (effect, _, failures, _) = pending;
        if (failures > 0)
        {
            await ConcludeAsync(pending, EffectResult.GaveUp, EffectReasons.Interrupted, failures, run, cancellationToken);
            return true;
        }

        if (!EscalationPath.TryParseDetail(effect.Detail, out var escalationId)
            || effect.RepositoryId is not { } repositoryId
            || effect.IssueNumber is not { } issueNumber
            || effect.TaskId is not { } taskId
            || await reads.FindEscalationAsync(escalationId, cancellationToken) is not { } escalation)
        {
            await ConcludeAsync(pending, EffectResult.Refused, EffectReasons.UnknownEffect, failures, run, cancellationToken);
            return true;
        }

        if (!await work.RecordEffectAsync(new EffectRecording(effect, Result: null, EffectReasons.Notifying, Failures: 1, NextAttemptAt: null), cancellationToken))
        {
            return true;
        }

        try
        {
            await notifier.NotifyAsync(new EscalationNotification(taskId, repositoryId, issueNumber, escalationId, escalation.Kind), effect.Key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The notifier is not GitHub, and what it does not do changes nothing: the tick has not failed, and the effect's record says so (FR-51).
            await ConcludeAsync(pending, EffectResult.GaveUp, EffectReasons.NotifierFailed, 1, run, cancellationToken);
            return true;
        }

        await EndAsync(effect, EffectResult.Applied, null, 1, run, cancellationToken);
        return true;
    }

    /// <summary>
    /// Pushes the bundle of a checkpoint to the Task's branch through the gateway's checks (AD-9, FR-20, FR-30). The intent names the tip before and the tip after, and the gateway reads the branch first, so a push that
    /// was made and not recorded is found and recorded, not made again (NFR-9). A check that fails ends the effect as refused, with the check as the reason, and raises an Escalation that names it; the Task's Workspace is not touched,
    /// which keeps it. Whatever the result, the bundle's file is deleted once the result is journaled, because nothing reads it again.
    /// </summary>
    private async Task<bool> AttemptPushAsync(PendingEffect pending, int retries, EffectsRun run, CancellationToken cancellationToken)
    {
        var (effect, found, failures, _) = pending;
        if (found is not { } task || branches is null || !PushIntent.TryParse(effect.Detail, out var intent))
        {
            // An intent that this version cannot make, or a host with no gateway for it: it is never going to be, so it ends as a refusal.
            await ConcludeAsync(pending, EffectResult.Refused, EffectReasons.UnknownEffect, failures, run, cancellationToken);
            return true;
        }

        var bundle = intent.Bundle(task.Id);
        if (task.State is TaskState.Failed or TaskState.Cancelled or TaskState.Withdrawn or TaskState.Merged or TaskState.Closed)
        {
            // A Task that has ended is pushed nothing; its Workspace is removed by the cleanup of an ended Task.
            await EndAsync(effect, EffectResult.Superseded, null, failures, run, cancellationToken);
            branches.DeleteBundle(bundle);
            return true;
        }

        PushResult result;
        try
        {
            var request = new PushRequest(task.RepositoryId, task.Id, intent.StepRunId, task.Branch, bundle, intent.StartCommit);
            result = await CallAsync(() => branches.PushAsync(request, effect.Key, cancellationToken), (_, _) => PushResult.Failed(MirrorFailure.Unreachable), run);
        }
        catch (ArgumentException)
        {
            // A branch that is not a Task branch, which the Task record never holds, or a bundle that names no prerequisite.
            await ConcludeAsync(pending, EffectResult.Refused, EffectReasons.UnknownEffect, failures, run, cancellationToken);
            branches.DeleteBundle(bundle);
            return true;
        }

        switch (result.Outcome)
        {
            case PushOutcome.Failed:
                // A retry keeps the bundle for the next attempt; an attempt that gave up, or was refused for good, is a result like any other.
                var ended = run.Ended;
                var goOn = await FailedAsync(pending, ToIssueFailure(result.Failure), waitUntil: null, retries, run, cancellationToken);
                if (run.Ended > ended)
                {
                    branches.DeleteBundle(bundle);
                }

                return goOn;

            case PushOutcome.Blocked:
                await ConcludeAsync(pending, EffectResult.Refused, result.Check!.Value.Tag(), failures, run, cancellationToken, EscalationPath.ForBlockedPush(task, result.Check.Value, result.Detail, effect.Cause, time));
                break;

            default:
                await EndAsync(effect, result.Outcome == PushOutcome.Pushed ? EffectResult.Applied : EffectResult.AlreadyInPlace, null, failures, run, cancellationToken);
                break;
        }

        branches.DeleteBundle(bundle);
        return true;
    }

    /// <summary>
    /// Publishes the pull request of a Task (FR-36, AD-5): one from the Task branch to the Base Branch, under Hephaestus's own identity, whose description Core composes from the facts the intent was journaled with. It is an effect of the loop and
    /// not a step run, so it holds no slot. The gateway looks for the branch's open pull request first, so one opened before a crash and not recorded is found and updated, never opened twice (FR-21), and a Task that is published again after further work
    /// has the same pull request updated. In the transaction that records the result the Task moves from <c>running</c> to <c>pull-request-open</c> and the intent of its state label is journaled; a Task that is already there only has its pull request
    /// brought up to date.
    /// </summary>
    private async Task<bool> AttemptPullRequestAsync(PendingEffect pending, int retries, Language language, EffectsRun run, CancellationToken cancellationToken)
    {
        var (effect, found, failures, _) = pending;
        if (found is not { } task || pullRequests is null || !PullRequestIntent.TryParse(effect.Detail, out _) || !PullRequestFacts.TryParse(effect.Payload, out var facts) || !BranchName.IsTaskBranch(task.Branch))
        {
            // An intent that this version cannot make, or a host with no gateway for it: it is never going to be, so it ends as a refusal.
            await ConcludeAsync(pending, EffectResult.Refused, EffectReasons.UnknownEffect, failures, run, cancellationToken);
            return true;
        }

        if (task.State is not (TaskState.Running or TaskState.PullRequestOpen))
        {
            // The Task has ended, or waits for something: it is published when it gets there again, by an intent of its own.
            await EndAsync(effect, EffectResult.Superseded, null, failures, run, cancellationToken);
            return true;
        }

        var project = (await reads.ListProjectsAsync(includeRemoved: true, cancellationToken)).FirstOrDefault(p => p.RepositoryId == task.RepositoryId);
        if (project is null)
        {
            await ConcludeAsync(pending, EffectResult.Refused, EffectReasons.UnknownEffect, failures, run, cancellationToken);
            return true;
        }

        var request = new PullRequestPublication(task.RepositoryId, task.Branch, project.BaseBranch, PullRequestDescription.Title(facts), PullRequestDescription.Compose(facts, task.IssueNumber, language));
        var result = await CallAsync(() => pullRequests.PublishAsync(request, effect.Key, cancellationToken), PullRequestResult.Failed, run);
        if (result.Failure != GitHubIssueFailure.None)
        {
            return await FailedAsync(pending, result.Failure, result.WaitUntil, retries, run, cancellationToken);
        }

        var move = task.State == TaskState.Running
            ? new TaskMove(TaskState.Running, TaskState.PullRequestOpen, [new NewEffect(task.Id, effect.Cause, EffectKind.Label, TaskState.PullRequestOpen.Tag())])
            : null;
        if (await work.RecordEffectAsync(new EffectRecording(effect, EffectResult.Applied, null, failures, NextAttemptAt: null, Move: move), cancellationToken))
        {
            run.Ended++;
            if (move is not null)
            {
                run.Moved[task.Id] = TaskState.PullRequestOpen;
            }
        }

        return true;
    }

    /// <summary>How the loop treats a push that could not be made for a reason that is not the work's: the same way as any effect on GitHub.</summary>
    private static GitHubIssueFailure ToIssueFailure(MirrorFailure failure) => failure switch
    {
        MirrorFailure.NotConnected => GitHubIssueFailure.NotConnected,
        MirrorFailure.KeyUnreadable => GitHubIssueFailure.KeyUnreadable,
        MirrorFailure.NotInstalled => GitHubIssueFailure.NotInstalled,
        MirrorFailure.Unreachable or MirrorFailure.GitFailed => GitHubIssueFailure.Unreachable,
        _ => GitHubIssueFailure.Rejected,
    };

    /// <summary>A gateway that throws is treated as one that did not answer: the attempt counts, and the exception is reported with the tick.</summary>
    private static async Task<T> CallAsync<T>(Func<Task<T>> call, Func<GitHubIssueFailure, DateTimeOffset?, T> failed, EffectsRun run)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Failure ??= exception;
            return failed(GitHubIssueFailure.Unreachable, null);
        }
    }

    private async Task<bool> FailedAsync(PendingEffect pending, GitHubIssueFailure failure, DateTimeOffset? waitUntil, int retries, EffectsRun run, CancellationToken cancellationToken)
    {
        var (effect, _, failures, _) = pending;
        var now = time.GetUtcNow();
        switch (failure)
        {
            case GitHubIssueFailure.RateLimited:
                // GitHub asked to wait: the wait is not a failure, and nothing else is sent until it is over.
                await RetryAsync(effect, EffectReasons.RateLimited, failures, waitUntil ?? now + RateLimitWait, run, cancellationToken);
                return false;

            case GitHubIssueFailure.Unreachable or GitHubIssueFailure.NotConnected or GitHubIssueFailure.KeyUnreadable:
                var reason = failure switch
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
                    failure switch
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
    /// Ends an effect that will not be made: the refusal or the give-up is journaled with its reason, and a label that could not be made raises
    /// an Escalation through the one path (<see cref="EscalationPath"/>), in the same transaction (AD-5): the Task moves to
    /// <c>waiting-for-owner</c>, and the comment says which effect it was. A Task that is not in a state
    /// that moves stays as it is. So does a Task whose comment could not be made: the comment says something about someone else's action, it
    /// is not the Task's own effect, and an issue that is locked, or that an outage kept from being written to, is no reason to stop work
    /// that is going well. It is the log that tells the Owner, and an error is logged either way, with the Task's id when there is one, the
    /// effect's key, and the repository and issue of a comment.
    /// </summary>
    private async Task ConcludeAsync(PendingEffect pending, EffectResult result, string reason, int failures, EffectsRun run, CancellationToken cancellationToken, EscalationPlan? blocked = null)
    {
        var (effect, task, _, _) = pending;
        var plan = blocked ?? (effect.Kind is EffectKind.Label or EffectKind.Push or EffectKind.PullRequest && task is not null ? EscalationPath.ForFailedEffect(task, effect, reason, time) : null);
        var recording = new EffectRecording(effect, result, reason, failures, NextAttemptAt: null, plan);
        if (await work.RecordEffectAsync(recording, cancellationToken))
        {
            run.Ended++;
            run.Problems.Add(new EffectProblem(task?.Id, effect.Key, result, reason, plan is not null, effect.RepositoryId, effect.IssueNumber));
            if (plan is not null)
            {
                run.Moved[task!.Id] = TaskState.WaitingForOwner;
            }
        }
    }
}
