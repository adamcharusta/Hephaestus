using Hephaestus.Core.Effects;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Escalations;

/// <summary>
/// Core's single escalation path (FR-48). Whatever cannot go on, a decision only the Owner can make or an effect that ended at its bound,
/// becomes the same plan: an Escalation recorded on the Task, the Task in <c>WaitingForOwner</c> with its resume condition, and the
/// intents of the state label, the comment on the issue, and the call to <c>INotifier</c>, to be committed together by the loop (AD-1, AD-5).
/// The path decides nothing about delivery: the comment is the Owner's alert in v0.1 (AR-63), and the Task's outcome is the same whether
/// or not the notifier does anything.
/// </summary>
public static class EscalationPath
{
    /// <summary>The prefix of the detail of an effect that is for an Escalation; the rest is the Escalation's id.</summary>
    public const string DetailPrefix = "escalation|";

    /// <summary>The plan for a question Hephaestus cannot answer. Null when the Task is in no state that moves to <c>WaitingForOwner</c>, which includes a Task that waits already: one Escalation asks for one decision.</summary>
    public static EscalationPlan? Raise(TaskRecord task, EscalationRequest request, string cause, TimeProvider time) =>
        Plan(task, EscalationKind.Decision, request.Decision, request.Options, cause, effect: null, check: null, ResumeConditions.TrustedReply, time);

    /// <summary>The plan for an effect that cannot be completed. Null when the Task is in no state that moves.</summary>
    public static EscalationPlan? ForFailedEffect(TaskRecord task, Effect effect, string reason, TimeProvider time) =>
        Plan(task, EscalationKind.EffectFailed, string.Empty, [], effect.Cause, new FailedEffect(effect.Key, effect.Kind, effect.Detail, reason), check: null, ResumeConditions.TrustedReply, time);

    /// <summary>
    /// The plan for work that was not pushed (AD-9, AD-19): the check that failed, and what it found, as facts the gateway gives. A Task branch that is not where Hephaestus left it waits for a Trusted User's <c>confirm</c>, which a
    /// reply does not replace; any other check waits for a reply. Null when the Task is in no state that moves.
    /// </summary>
    /// <param name="cause">What caused it: the push effect's cause, or the step run that found the branch moved.</param>
    public static EscalationPlan? ForBlockedPush(TaskRecord task, PushCheck check, string? detail, string cause, TimeProvider time) =>
        Plan(task, EscalationKind.PushBlocked, EscalationRequest.OneLine(detail, EscalationRequest.MaximumDecisionLength), [], cause, effect: null, check, check == PushCheck.RemoteMoved ? ResumeConditions.Confirm : ResumeConditions.TrustedReply, time);

    private static EscalationPlan? Plan(TaskRecord task, EscalationKind kind, string decision, IReadOnlyList<string> options, string cause, FailedEffect? effect, PushCheck? check, string resumeCondition, TimeProvider time)
    {
        if (!task.State.MovesToWaitingForOwnerWhenItCannotGoOn())
        {
            return null;
        }

        var escalation = new Escalation(EntityId.New(time), task.Id, kind, decision, options, resumeCondition, cause, time.GetUtcNow(), effect, Check: check);
        var detail = DetailPrefix + escalation.Id.ToString("D");
        return new EscalationPlan(
            escalation,
            task.State,
            [
                new NewEffect(task.Id, cause, EffectKind.Label, TaskState.WaitingForOwner.Tag()),
                new NewEffect(task.Id, cause, EffectKind.Comment, detail, task.RepositoryId, task.IssueNumber),
                new NewEffect(task.Id, cause, EffectKind.Notification, detail, task.RepositoryId, task.IssueNumber),
            ]);
    }

    /// <summary>The id of the Escalation an effect's detail names; false when the detail is of something else.</summary>
    public static bool TryParseDetail(string detail, out Guid escalationId)
    {
        escalationId = default;
        return detail.StartsWith(DetailPrefix, StringComparison.Ordinal) && Guid.TryParseExact(detail[DetailPrefix.Length..], "D", out escalationId);
    }
}
