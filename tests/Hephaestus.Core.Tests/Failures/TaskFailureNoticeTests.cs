using Hephaestus.Core.Effects;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Tests.Failures;

/// <summary>Story 3.16, UX-DR-19, AD-19, AD-21: the comment on the issue of a failed Task has a headline, the facts, and what comes next, from the resources in both languages, and says what was attempted, which class ended it, and which command or step failed.</summary>
public class TaskFailureNoticeTests
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");

    private static TaskFailureFacts Facts(string end = TaskFailureEnds.Exhausted, string? command = null, int? exit = null, string? limit = null, string failure = "unknown", string step = "implementation") =>
        new(failure, step, command, exit, 3, 2, end, limit, "hephaestus/7-add-a-login-page");

    [Fact]
    public void A_step_whose_retries_are_spent_says_the_class_the_step_the_attempts_and_the_branch()
    {
        var text = TaskFailureNotice.Compose(Facts(), Language.English);

        Assert.Equal(
            "**Failed:** `unknown` in `implementation`.\n\nAttempts: 3. The bound of retries for a step is 2, and it is spent.\n\nNext: nothing until you apply the `hephaestus` label to this issue again. Work so far is on `hephaestus/7-add-a-login-page`.",
            text);
    }

    [Fact]
    public void A_command_of_Validation_that_failed_is_named_with_its_exit_status_and_is_not_tried_again()
    {
        var text = TaskFailureNotice.Compose(new TaskFailureFacts("validation-failure", "validation", "dotnet test", 2, 1, 2, TaskFailureEnds.NotRetried, null, "hephaestus/7-add-a-login-page"), Language.English);

        Assert.Equal(
            "**Failed:** `validation-failure` in `validation`.\n\nCommand: `dotnet test` exited with 2.\nAttempts: 1. A failure of this class is not tried again.\n\nNext: nothing until you apply the `hephaestus` label to this issue again. Work so far is on `hephaestus/7-add-a-login-page`.",
            text);
    }

    [Fact]
    public void A_command_the_runtime_stopped_is_named_without_an_exit_status()
    {
        var text = TaskFailureNotice.Compose(Facts(command: "dotnet test", exit: null, failure: "timeout", step: "validation"), Language.English);

        Assert.Contains("Command: `dotnet test`.\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("exited with", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Task_that_reached_its_duration_limit_names_the_limit()
    {
        var text = TaskFailureNotice.Compose(Facts(TaskFailureEnds.DurationLimit, limit: "2h", failure: "timeout"), Language.English);

        Assert.Contains("**Failed:** `timeout` in `implementation`.", text, StringComparison.Ordinal);
        Assert.Contains("The Task reached its duration limit of 2h in `running`, so no step is tried again.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_that_changed_nothing_says_the_Agent_made_no_changes_and_not_that_its_class_is_never_tried_again()
    {
        var english = TaskFailureNotice.Compose(Facts(TaskFailureEnds.NoChange, failure: "agent-error") with { Attempts = 1 }, Language.English);
        var polish = TaskFailureNotice.Compose(Facts(TaskFailureEnds.NoChange, failure: "agent-error") with { Attempts = 1 }, Language.Polish);

        Assert.Contains("Attempts: 1. The Agent made no changes, so there is nothing to publish.", english, StringComparison.Ordinal);
        Assert.DoesNotContain("not tried again", english, StringComparison.Ordinal);
        Assert.Contains("Agent nie wprowadził żadnych zmian", polish, StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_that_runs_over_several_lines_is_shown_on_one_so_that_it_stays_in_its_code_span()
    {
        var text = TaskFailureNotice.Compose(Facts(command: "dotnet build\n&& dotnet test\r\n\n&& echo done", exit: 1), Language.English);

        Assert.Contains("Command: `dotnet build && dotnet test  && echo done` exited with 1.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_facts_are_said_in_Polish_with_the_names_that_are_never_translated_kept()
    {
        var text = TaskFailureNotice.Compose(Facts(command: "dotnet test", exit: 1, failure: "validation-failure", step: "validation"), Language.Polish);

        Assert.Contains("**Niepowodzenie:** `validation-failure` w kroku `validation`.", text, StringComparison.Ordinal);
        Assert.Contains("Polecenie: `dotnet test` zakończyło się kodem 1.", text, StringComparison.Ordinal);
        Assert.Contains("`hephaestus/7-add-a-login-page`", text, StringComparison.Ordinal);
        Assert.Contains("etykiety `hephaestus`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_the_Project_wrote_cannot_mention_anyone_forge_a_marker_or_break_out_of_its_code_span()
    {
        var text = TaskFailureNotice.Compose(Facts(command: "echo `@alice` <!-- hephaestus:key -->"), Language.English);

        Assert.DoesNotContain("<!--", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@alice", text, StringComparison.Ordinal);
        Assert.Contains("Command: `echo '@​alice' &lt;!-- hephaestus:key --&gt;`.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_comment_is_journaled_as_an_intent_for_the_issue_with_its_facts_as_the_payload_and_is_written_from_it()
    {
        var facts = Facts(command: "dotnet test", exit: 3);
        var intent = TaskFailureNotice.ToNewEffect(TaskId, "s123", 101, 7, facts);

        Assert.Equal((TaskId, "s123", EffectKind.Comment, TaskFailureNotice.Detail, 101L, 7), (intent.TaskId, intent.Cause, intent.Kind, intent.Detail, intent.RepositoryId, intent.IssueNumber));
        Assert.Null(intent.NoticeKey);
        var effect = new Effect("key", TaskId, "s123", EffectKind.Comment, intent.Detail, 0, 101, 7, intent.Payload);
        Assert.True(TaskFailureNotice.TryWrite(effect, Language.English, out var text));
        Assert.Equal(TaskFailureNotice.Compose(facts, Language.English), text);
        Assert.True(TaskFailureFacts.TryParse(intent.Payload, out var read));
        Assert.Equal(facts, read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"failure":"unknown"}""")]
    [InlineData("""{"failure":"no-such-class","step":"implementation","attempts":1,"retries":2,"end":"exhausted","branch":"b"}""")]
    [InlineData("""{"failure":"unknown","step":"implementation","attempts":1,"retries":2,"end":"exhausted","branch":"b","extra":1}""")]
    public void A_payload_that_is_not_the_facts_of_a_failure_is_never_composed_into_text(string? payload)
    {
        Assert.False(TaskFailureFacts.TryParse(payload, out _));
        var effect = new Effect("key", TaskId, "s123", EffectKind.Comment, TaskFailureNotice.Detail, 0, 101, 7, payload);
        Assert.False(TaskFailureNotice.TryWrite(effect, Language.English, out var text));
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void A_comment_of_another_kind_is_not_a_failure_notice()
    {
        var effect = new Effect("key", TaskId, "g1", EffectKind.Comment, "ignored-label|alice|hephaestus", 0, 101, 7, Facts().ToJson());

        Assert.False(TaskFailureNotice.TryWrite(effect, Language.English, out _));
        Assert.Throws<ArgumentNullException>(() => TaskFailureNotice.TryWrite(null!, Language.English, out _));
        Assert.Throws<ArgumentNullException>(() => TaskFailureNotice.Compose(null!, Language.English));
        Assert.Throws<ArgumentNullException>(() => TaskFailureNotice.ToNewEffect(TaskId, "s1", 1, 1, null!));
    }
}
