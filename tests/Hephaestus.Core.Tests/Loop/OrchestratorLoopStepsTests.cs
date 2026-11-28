using System.Text;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// Story 3.14, AD-1, AD-9, AD-13, AD-18, FR-17 to FR-23, FR-31, FR-36: a Running Task is carried through its steps from what is recorded: the Primary Agent implements on a fresh checkout, the work is checkpointed and pushed, each Validation command runs on the
/// pushed tip, and on a pass the pull request is published. The loop never waits for a container: the step-exited Signal ends a step run, and every decision after it is made from the records.
/// </summary>
public class OrchestratorLoopStepsTests
{
    private static CancellationToken Token => RunRig.Token;

    private static string TaskJson(StepContainerRequest request) => Encoding.UTF8.GetString(request.Context.Single(file => file.Path == "task.json").Content);

    [Fact]
    public async Task An_Implementation_step_starts_on_a_fresh_checkout_of_the_start_commit_with_the_snapshot_the_adapter_described_and_the_context_of_the_step()
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        await rig.StartAsync();

        await rig.TickAsync();

        var task = rig.Task();
        var step = rig.LastStep();
        Assert.Equal((TaskState.Running, StepKind.Implementation, StepRunStatus.Started), (task.State, step.Kind, step.Status));
        var checkout = Assert.Single(rig.Runtime.Checkouts).Request;
        Assert.Equal((task.Id, step.Id, task.Branch, RunRig.StartCommit), (checkout.TaskId, checkout.StepRunId, checkout.Branch, checkout.Bundle.Tip));
        var (container, key) = Assert.Single(rig.Runtime.Steps);
        Assert.Equal(FakeRuntime.Image.Digest, container.Image.Digest);
        Assert.Equal(["/hephaestus/agents/fake/fake", "run"], container.Command);
        Assert.Equal("fake-api-key", Assert.Single(container.SecretReferences!).Name);
        Assert.Equal(step.Deadline, container.Deadline);
        Assert.Null(container.AllowedEgressHosts);
        Assert.Contains(container.Context, file => file.Path == "instruction.md");
        Assert.Contains(container.Context, file => file.Path == "issue.md" && Encoding.UTF8.GetString(file.Content).Contains("Add a login page", StringComparison.Ordinal));
        Assert.Contains(container.Methodology, file => file.Path == "implementation.md");
        Assert.Contains(RunRig.StartCommit, TaskJson(container), StringComparison.Ordinal);
        Assert.Contains("\"baseBranch\": \"main\"", TaskJson(container), StringComparison.Ordinal);
        Assert.StartsWith($"{task.Id:D}:", key, StringComparison.Ordinal);
        Assert.Equal(task.Id, Assert.Single(rig.Runtime.Volumes).TaskId);
        Assert.Equal((Agent.ClaudeCode, "claude-opus-5-5", Effort.High), (step.Agent!.Value, step.Model, step.Effort!.Value));
        Assert.Equal(("claude-opus-5-5", Effort.High, "instruction.md"), (rig.Adapter.Invocations[0].Model, rig.Adapter.Invocations[0].Effort!.Value, rig.Adapter.Invocations[0].InstructionPath));
    }

    [Fact]
    public async Task The_end_of_the_container_is_applied_as_the_result_of_the_Agent_and_the_work_is_checkpointed_as_one_commit_and_its_push_is_journaled_with_it()
    {
        var rig = new RunRig();

        var implementation = await rig.ImplementAsync();

        var ended = rig.Steps()[0];
        Assert.Equal((StepRunStatus.Checkpointed, "completed", null, 0), (ended.Status, ended.Outcome, ended.Failure, ended.ExitCode));
        Assert.Equal((StepExitCause.Exited, rig.Runtime.NextTip, RunRig.StartCommit), (ended.Cause, ended.TipAfter, ended.TipBefore));
        var (checkpoint, _) = Assert.Single(rig.Runtime.Checkpoints);
        Assert.Equal((StepKind.Implementation, RunRig.StartCommit), (checkpoint.Kind, checkpoint.Parent.Tip));
        Assert.Contains("Hephaestus-Agent: claude-code", checkpoint.Commit.Message, StringComparison.Ordinal);
        Assert.Contains("Hephaestus-Model: claude-opus-5-5", checkpoint.Commit.Message, StringComparison.Ordinal);
        Assert.Contains("Hephaestus-Effort: high", checkpoint.Commit.Message, StringComparison.Ordinal);
        Assert.Equal("hephaestus-test[bot]", checkpoint.Commit.Identity.Name);
        var push = Assert.Single(rig.Stores.Journal, e => e.Entry == JournalEntryKind.Intent && e.Effect.Kind == EffectKind.Push);
        Assert.True(PushIntent.TryParse(push.Effect.Detail, out var intent));
        Assert.Equal((implementation.Id, rig.Runtime.NextTip, RunRig.StartCommit, RunRig.StartCommit), (intent.StepRunId, intent.Tip, intent.LastPushedTip, intent.StartCommit));
    }

    [Fact]
    public async Task Validation_runs_each_command_alone_on_the_pushed_tip_after_the_push_is_made_and_never_before()
    {
        var rig = new RunRig();
        var implementation = await rig.ImplementAsync();

        // The push is an effect of the effects phase, made in the tick that journaled it; Validation starts in the next tick, on what was pushed.
        Assert.Empty(rig.Runtime.Validations);
        Assert.Single(rig.Branches.Pushes);
        await rig.TickAsync();

        var (first, _) = Assert.Single(rig.Runtime.Validations);
        Assert.Equal(("dotnet build", rig.Runtime.NextTip), (first.Command, first.Tip.Tip));
        Assert.Equal(rig.Task().Branch, first.Branch);
        Assert.Equal(StepKind.Validation, rig.LastStep().Kind);
        Assert.Equal(0, rig.LastStep().CommandIndex);
        Assert.Single(rig.Branches.Pushes);
        await rig.EndValidationAsync();
        Assert.Equal(["dotnet build", "dotnet test"], rig.Runtime.Validations.Select(v => v.Request.Command));
        Assert.Equal(implementation.Id, rig.Steps()[0].Id);
    }

    [Fact]
    public async Task When_every_command_passes_the_pull_request_is_published_the_Task_leaves_Running_and_the_window_closes()
    {
        var rig = new RunRig();
        await rig.ImplementAsync();
        await rig.TickAsync();
        await rig.EndValidationAsync();
        await rig.EndValidationAsync();

        await rig.TickAsync(3);

        var task = rig.Task();
        Assert.Equal(TaskState.PullRequestOpen, task.State);
        var (request, _) = Assert.Single(rig.Pulls.Publications);
        Assert.Equal((task.Branch, "main", "Add a login page"), (request.Head, request.Base, request.Title));
        Assert.Contains("Added a login page.", request.Body, StringComparison.Ordinal);
        Assert.Contains("dotnet build", request.Body, StringComparison.Ordinal);
        Assert.Contains("dotnet test", request.Body, StringComparison.Ordinal);
        Assert.Equal(StepRunStatus.Concluded, rig.LastStep().Status);
        Assert.All(rig.Stores.Windows, window => Assert.Equal(Hephaestus.Core.Windows.WindowEnd.Drained, window.End));
        Assert.Contains("hephaestus:pull-request-open", rig.Stores.Github.LabelsOf(task.RepositoryId, task.IssueNumber));
    }

    [Fact]
    public async Task The_Task_holds_the_single_slot_through_all_its_steps_and_the_next_Task_starts_only_when_it_has_left_Running()
    {
        var rig = new RunRig();
        await rig.QueueAsync(issue: 3, title: "First");
        await rig.QueueAsync(eventId: "issue-event:2", issue: 9, title: "Second");
        await rig.StartAsync();
        await rig.TickAsync();
        var first = rig.Task(3);
        await rig.ExitAsync(rig.LastStep(3).Id, 0, StepExitCause.Exited, RunRig.Completed());
        await rig.TickAsync(3);

        Assert.Equal((TaskState.Running, TaskState.Queued), (rig.Task(3).State, rig.Task(9).State));
        Assert.Empty(rig.Steps(9));
        await rig.EndValidationAsync(issue: 3);
        Assert.Equal((TaskState.Running, TaskState.Queued), (rig.Task(3).State, rig.Task(9).State));
        await rig.EndValidationAsync(issue: 3);
        await rig.TickAsync(3);

        Assert.Equal(first.Id, rig.Task(3).Id);
        Assert.Equal((TaskState.PullRequestOpen, TaskState.Running), (rig.Task(3).State, rig.Task(9).State));
        Assert.Equal(StepKind.Implementation, rig.LastStep(9).Kind);
    }

    [Fact]
    public async Task A_failed_command_ends_the_Task_as_Failed_with_its_label_and_no_pull_request_and_the_commands_after_it_do_not_run()
    {
        var rig = new RunRig();
        await rig.ImplementAsync();
        await rig.TickAsync();

        await rig.EndValidationAsync(exitCode: 2);
        await rig.TickAsync(2);

        Assert.Equal(TaskState.Failed, rig.Task().State);
        Assert.Equal(("failed", "validation-failure", 2), (rig.LastStep().Outcome, rig.LastStep().Failure, rig.LastStep().ExitCode));
        Assert.Single(rig.Runtime.Validations);
        Assert.Empty(rig.Pulls.Publications);
        Assert.Contains("hephaestus:failed", rig.Stores.Github.LabelsOf(RunRig.RepositoryId, RunRig.Issue));
    }

    [Theory]
    [InlineData(2, StepExitCause.Exited, "unknown")]
    [InlineData(0, StepExitCause.Timeout, "timeout")]
    [InlineData(0, StepExitCause.OutOfMemory, "infrastructure-error")]
    [InlineData(0, StepExitCause.Lost, "infrastructure-error")]
    public async Task A_step_that_does_not_end_well_ends_the_Task_with_the_class_the_runtime_and_the_adapter_name_and_keeps_its_branch_unpushed(int exitCode, StepExitCause cause, string failure)
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();

        await rig.ExitAsync(rig.LastStep().Id, exitCode, cause, RunRig.Completed());
        await rig.TickAsync();

        var step = rig.LastStep();
        Assert.Equal((TaskState.Failed, StepRunStatus.Ended, failure), (rig.Task().State, step.Status, step.Failure));
        Assert.Empty(rig.Runtime.Checkpoints);
        Assert.Empty(rig.Branches.Pushes);
    }

    [Fact]
    public async Task A_class_the_adapter_names_for_a_CLI_that_ended_by_itself_is_recorded_as_it_is()
    {
        var rig = new RunRig();
        rig.Adapter.Class = Hephaestus.Core.Failures.FailureClass.UsageLimit;
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();

        await rig.ExitAsync(rig.LastStep().Id, 1, StepExitCause.Exited);
        await rig.TickAsync();

        Assert.Equal("usage-limit", rig.LastStep().Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"outcome":"completed"}""")]
    [InlineData("""{"outcome":"completed","summary":"x","questions":[],"extra":1}""")]
    [InlineData("""{"outcome":"finished","summary":"x","questions":[]}""")]
    [InlineData("""{"outcome":"needs-decision","summary":"x","questions":[]}""")]
    public async Task A_result_that_is_missing_or_not_whole_is_an_agent_error_and_is_never_interpreted(string? result)
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();

        await rig.ExitAsync(rig.LastStep().Id, 0, StepExitCause.Exited, result is null ? ["no result here"] : ["RESULT:" + result]);
        await rig.TickAsync();

        Assert.Equal((TaskState.Failed, "agent-error", null), (rig.Task().State, rig.LastStep().Failure, rig.LastStep().Outcome));
        Assert.Empty(rig.Runtime.Checkpoints);
    }

    [Fact]
    public async Task A_result_that_needs_a_decision_raises_an_Escalation_with_its_question_and_options_and_nothing_is_checkpointed()
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();

        await rig.ExitAsync(rig.LastStep().Id, 0, StepExitCause.Exited, RunRig.NeedsDecision());
        await rig.TickAsync();

        Assert.Equal(TaskState.WaitingForOwner, rig.Task().State);
        var escalation = Assert.Single(rig.Stores.Escalations);
        Assert.Equal(("Which database?", EscalationKind.Decision), (escalation.Decision, escalation.Kind));
        Assert.Equal(["Postgres", "SQLite"], escalation.Options);
        Assert.Equal("needs-decision", rig.LastStep().Outcome);
        Assert.Empty(rig.Runtime.Checkpoints);
    }

    [Fact]
    public async Task A_tree_the_Agent_left_as_it_found_it_with_nothing_pushed_ends_the_Task_and_runs_no_Validation()
    {
        var rig = new RunRig();
        rig.Runtime.Unchanged = true;

        await rig.ImplementAsync();
        await rig.TickAsync(2);

        Assert.Equal(TaskState.Failed, rig.Task().State);
        Assert.Equal(("agent-error", StepRunStatus.Concluded), (rig.LastStep().Failure, rig.LastStep().Status));
        Assert.Empty(rig.Runtime.Validations);
        Assert.Empty(rig.Pulls.Publications);
        Assert.Empty(rig.Branches.Pushes);
    }

    [Fact]
    public async Task A_runtime_that_cannot_make_the_checkout_ends_the_step_and_the_Task_instead_of_leaving_the_slot_held()
    {
        var rig = new RunRig();
        rig.Runtime.Throws[nameof(FakeRuntime.CheckoutAsync)] = new WorkspaceRuntimeException(new Hephaestus.Core.Localization.Message("workspace.probe.docker"));
        await rig.QueueAsync();
        await rig.StartAsync();

        var report = await rig.TickAsync();

        var step = rig.LastStep();
        Assert.Equal((TaskState.Failed, StepRunStatus.Ended, "infrastructure-error"), (rig.Task().State, step.Status, step.Failure));
        Assert.Empty(rig.Runtime.Steps);
        Assert.Contains(report.TaskProblems!, line => line.Contains("infrastructure-error", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(MirrorFailure.Unreachable, false)]
    [InlineData(MirrorFailure.CommitMissing, true)]
    public async Task A_commit_that_cannot_be_bundled_ends_the_step_as_an_infrastructure_error(MirrorFailure failure, bool bundle)
    {
        var rig = new RunRig();
        if (bundle)
        {
            rig.Mirrors.BundleFails = failure;
        }
        else
        {
            rig.Mirrors.FetchFails = failure;
        }

        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();

        Assert.Equal((TaskState.Failed, "infrastructure-error"), (rig.Task().State, rig.LastStep().Failure));
        Assert.Empty(rig.Runtime.Checkouts);
    }

    [Fact]
    public async Task The_Task_branch_is_looked_at_before_every_step_against_the_tip_last_pushed_and_the_start_commit()
    {
        var rig = new RunRig();
        await rig.ImplementAsync();
        await rig.TickAsync();

        Assert.Equal([RunRig.StartCommit, rig.Runtime.NextTip], rig.Branches.Checks.Select(check => check.LastPushedTip));
        Assert.All(rig.Branches.Checks, check => Assert.Equal((rig.Task().Branch, RunRig.StartCommit, RunRig.RepositoryId), (check.Branch, check.StartCommit, check.RepositoryId)));
    }

    [Fact]
    public async Task A_branch_that_someone_else_moved_holds_the_Task_for_the_Owner_and_no_step_runs_on_it()
    {
        var rig = new RunRig();
        rig.Branches.Check = BranchCheckResult.Moved("2 commits Hephaestus did not push");
        await rig.QueueAsync();
        await rig.StartAsync();

        var report = await rig.TickAsync();

        Assert.Equal(TaskState.WaitingForOwner, rig.Task().State);
        var escalation = Assert.Single(rig.Stores.Escalations);
        Assert.Equal((EscalationKind.PushBlocked, PushCheck.RemoteMoved, ResumeConditions.Confirm), (escalation.Kind, escalation.Check, escalation.ResumeCondition));
        Assert.Equal(("branch-moved", StepRunStatus.Ended, null), (rig.LastStep().Outcome, rig.LastStep().Status, rig.LastStep().Failure));
        Assert.Empty(rig.Runtime.Checkouts);
        Assert.Empty(rig.Runtime.Steps);
        Assert.Contains(report.TaskProblems!, line => line.Contains("not where Hephaestus left it", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_branch_that_cannot_be_read_ends_the_step_as_an_infrastructure_error()
    {
        var rig = new RunRig();
        rig.Branches.Check = BranchCheckResult.Unavailable(MirrorFailure.Unreachable);
        await rig.QueueAsync();
        await rig.StartAsync();

        await rig.TickAsync();

        Assert.Equal((TaskState.Failed, "infrastructure-error"), (rig.Task().State, rig.LastStep().Failure));
        Assert.Empty(rig.Runtime.Checkouts);
    }

    [Fact]
    public async Task The_end_of_a_container_is_recorded_once_and_a_second_Signal_for_it_changes_nothing()
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();
        var stepRun = rig.LastStep().Id;

        await rig.ExitAsync(stepRun, 0, StepExitCause.Exited, RunRig.Completed());
        await rig.TickAsync();
        var after = rig.Steps()[0];
        await rig.Stores.AppendAsync([Hephaestus.Core.Signals.Signal.StepExited(stepRun, 7, StepExitCause.Exited, rig.Time.Now)], Token);
        await rig.TickAsync();

        Assert.Equal(after, rig.Steps()[0]);
        Assert.Equal(0, rig.Steps()[0].ExitCode);
    }

    [Fact]
    public async Task The_end_of_a_container_nobody_planned_is_applied_with_no_change()
    {
        var rig = new RunRig();

        await rig.Stores.AppendAsync([Hephaestus.Core.Signals.Signal.StepExited(Guid.CreateVersion7(), 0, StepExitCause.Exited, rig.Time.Now)], Token);
        var report = await rig.TickAsync();

        Assert.Equal((1, 0), (report.Applied, report.Rejected));
        Assert.Empty(rig.Stores.StepRuns);
    }

    [Fact]
    public async Task A_loop_that_starts_again_carries_the_Task_on_from_the_records_alone()
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();
        var stepRun = rig.LastStep().Id;
        await rig.ExitAsync(stepRun, 0, StepExitCause.Exited, RunRig.Completed());

        // The host stopped and started: nothing but the stores is the same.
        var restarted = new Hephaestus.Core.Loop.OrchestratorLoop(rig.Stores, rig.Stores, rig.Stores, rig.Stores.Github, rig.Stores.Notifier, rig.Time, new Hephaestus.Core.Loop.LoopHeartbeat(rig.Time), branches: rig.Branches, pullRequests: rig.Pulls, runtime: rig.Runtime, mirrors: rig.Mirrors, methodologies: new FakeMethodologies(), output: rig.Output, adapters: [rig.Adapter]);
        await restarted.TickAsync(Token);
        await restarted.TickAsync(Token);

        Assert.Equal(StepRunStatus.Checkpointed, rig.Steps()[0].Status);
        Assert.Single(rig.Stores.Journal, e => e.Entry == JournalEntryKind.Intent && e.Effect.Kind == EffectKind.Push);
        Assert.Single(rig.Runtime.Validations);
    }

    [Fact]
    public async Task A_step_run_that_was_planned_and_not_started_is_started_by_the_next_tick_and_not_twice()
    {
        var rig = new RunRig();
        rig.Runtime.Throws[nameof(FakeRuntime.StartStepAsync)] = new IOException("the Docker socket is gone");
        await rig.QueueAsync();
        await rig.StartAsync();

        var report = await rig.TickAsync();

        Assert.NotNull(report.Failure);
        Assert.Equal((TaskState.Running, StepRunStatus.Planned), (rig.Task().State, rig.LastStep().Status));
        rig.Runtime.Throws.Clear();
        await rig.TickAsync();
        Assert.Equal(StepRunStatus.Started, rig.LastStep().Status);
        Assert.Single(rig.Steps());
    }

    [Fact]
    public async Task Every_later_step_uses_the_snapshot_even_after_the_file_changes_on_the_Base_Branch()
    {
        var rig = new RunRig();
        await rig.ImplementAsync();
        rig.SetConfiguration(RunRig.RepositoryId, "primaryAlias: opus\nvalidation:\n  - echo changed\nimage: alpine:3\n", new string('f', 40));

        await rig.TickAsync();
        await rig.EndValidationAsync();

        Assert.Equal(["dotnet build", "dotnet test"], rig.Runtime.Validations.Select(v => v.Request.Command));
        Assert.Equal(1, rig.Stores.Github.FileReads);
        Assert.Equal(FakeRuntime.Image.Digest, rig.Runtime.Validations[0].Request.Image.Digest);
    }
}
