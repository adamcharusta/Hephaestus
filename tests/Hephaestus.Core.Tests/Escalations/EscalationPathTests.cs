using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Escalations;

/// <summary>FR-48, AD-1, AD-5: the one path that raises an Escalation makes one plan, whatever cannot go on.</summary>
public class EscalationPathTests
{
    private static readonly MovingTime Time = new();

    private static TaskRecord Task(TaskState state) => new(Guid.CreateVersion7(), 101, 7, state, "hephaestus/7-add-a-login-page", Time.Now, Time.Now);

    [Fact]
    public void A_decision_makes_an_Escalation_with_its_resume_condition_and_the_intents_that_show_it()
    {
        var task = Task(TaskState.Running);

        var plan = EscalationPath.Raise(task, EscalationRequest.For("Which database?", ["SQLite", "Postgres"]), "s4", Time)!;

        Assert.Equal((TaskState.Running, TaskState.WaitingForOwner), (plan.From, plan.To));
        var escalation = plan.Escalation;
        Assert.Equal((task.Id, EscalationKind.Decision, "Which database?", ResumeConditions.TrustedReply, "s4", Time.Now), (escalation.TaskId, escalation.Kind, escalation.Decision, escalation.ResumeCondition, escalation.Cause, escalation.RaisedAt));
        Assert.Equal(["SQLite", "Postgres"], escalation.Options);
        Assert.Null(escalation.ClosedAt);
        var detail = EscalationPath.DetailPrefix + escalation.Id.ToString("D");
        Assert.Equal(
            [
                new NewEffect(task.Id, "s4", EffectKind.Label, "waiting-for-owner"),
                new NewEffect(task.Id, "s4", EffectKind.Comment, detail, 101, 7),
                new NewEffect(task.Id, "s4", EffectKind.Notification, detail, 101, 7),
            ],
            plan.Effects);
    }

    [Fact]
    public void An_effect_that_cannot_be_completed_makes_the_same_plan_and_names_the_effect()
    {
        var task = Task(TaskState.Queued);
        var effect = new Effect($"{task.Id}:g1:label:0", task.Id, "g1", EffectKind.Label, "queued", 0);

        var plan = EscalationPath.ForFailedEffect(task, effect, EffectReasons.Unreachable, Time)!;

        Assert.Equal((EscalationKind.EffectFailed, "g1"), (plan.Escalation.Kind, plan.Escalation.Cause));
        Assert.Equal(new FailedEffect(effect.Key, EffectKind.Label, "queued", EffectReasons.Unreachable), plan.Escalation.Effect);
        Assert.Equal([EffectKind.Label, EffectKind.Comment, EffectKind.Notification], plan.Effects.Select(e => e.Kind));
    }

    [Theory]
    [InlineData(TaskState.Queued)]
    [InlineData(TaskState.Running)]
    [InlineData(TaskState.WaitingForCapacity)]
    [InlineData(TaskState.PullRequestOpen)]
    public void A_Task_in_a_state_that_moves_is_escalated(TaskState state) =>
        Assert.NotNull(EscalationPath.Raise(Task(state), EscalationRequest.For("Which?", []), "s1", Time));

    [Theory]
    [InlineData(TaskState.WaitingForOwner)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.Cancelled)]
    [InlineData(TaskState.Withdrawn)]
    [InlineData(TaskState.Merged)]
    [InlineData(TaskState.Closed)]
    public void A_Task_that_waits_already_or_has_ended_is_not_escalated_so_that_one_Escalation_asks_for_one_decision(TaskState state)
    {
        var task = Task(state);

        Assert.Null(EscalationPath.Raise(task, EscalationRequest.For("Which?", []), "s1", Time));
        Assert.Null(EscalationPath.ForFailedEffect(task, new Effect("k", task.Id, "g1", EffectKind.Label, "queued", 0), EffectReasons.Rejected, Time));
    }

    [Fact]
    public void The_detail_of_an_effect_names_its_Escalation()
    {
        var id = Guid.CreateVersion7();

        Assert.True(EscalationPath.TryParseDetail(EscalationPath.DetailPrefix + id.ToString("D"), out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData("ignored-label|bob|hephaestus")]
    [InlineData("escalation|")]
    [InlineData("escalation|not-a-guid")]
    [InlineData("")]
    public void A_detail_of_something_else_names_no_Escalation(string detail) => Assert.False(EscalationPath.TryParseDetail(detail, out _));
}
