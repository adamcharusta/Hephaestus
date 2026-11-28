using Hephaestus.Core.Effects;
using Hephaestus.Core.Failures;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// Story 3.16, AD-11, NFR-10, NFR-5, FR-20, AR-55: a step that fails in a class that spends the step retry bound is attempted again as a new step run on a fresh checkout of the last pushed tip, its work is kept on the Task branch first, the bound ends the Task with a
/// comment on its issue, and no step outlives the Task's time in Running.
/// </summary>
public class OrchestratorLoopRetryTests
{
    private static readonly string Pushed = new('e', 40);

    private static string LimitedTo(string duration) => RunRig.ConfigurationFile + $"\nlimits:\n  taskDuration: {duration}\n";

    /// <summary>A Task whose Implementation is running, with the bound of retries given.</summary>
    private static async Task<RunRig> RunningAsync(int retries = 2, string? configuration = null)
    {
        var rig = configuration is null ? new RunRig() : new RunRig(configuration);
        rig.SetStepRetries(retries);
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();
        return rig;
    }

    /// <summary>The running step ends with a failure and the loop keeps its work and decides: two ticks, since the push is made in the effects phase of the first.</summary>
    private static async Task FailAsync(RunRig rig, int exitCode = 2, StepExitCause cause = StepExitCause.Exited, params string[] output)
    {
        await rig.ExitAsync(rig.LastStep().Id, exitCode, cause, output);
        await rig.TickAsync(2);
    }

    private static string CommentOf(RunRig rig) => Assert.Single(rig.Stores.Github.Comments).Text;

    [Theory]
    [InlineData(2, StepExitCause.Exited, "unknown")]
    [InlineData(0, StepExitCause.Exited, "agent-error")]
    [InlineData(0, StepExitCause.Timeout, "timeout")]
    [InlineData(0, StepExitCause.OutOfMemory, "infrastructure-error")]
    [InlineData(0, StepExitCause.Lost, "infrastructure-error")]
    public async Task A_step_that_fails_in_a_class_that_spends_the_bound_is_attempted_again_as_a_new_step_run_on_a_fresh_checkout_of_the_last_pushed_tip(int exitCode, StepExitCause cause, string failure)
    {
        var rig = await RunningAsync();
        rig.Runtime.NextTip = Pushed;
        var first = rig.LastStep();

        await FailAsync(rig, exitCode, cause, exitCode == 0 && cause == StepExitCause.Exited ? ["no result here"] : [RunRig.Completed()]);

        var steps = rig.Steps();
        Assert.Equal(2, steps.Count);
        Assert.Equal((StepRunStatus.Concluded, failure, 1, Pushed), (steps[0].Status, steps[0].Failure, steps[0].Attempt, steps[0].TipAfter));
        var retry = steps[1];
        Assert.NotEqual(first.Id, retry.Id);
        Assert.Equal((TaskState.Running, StepKind.Implementation, StepRunStatus.Started, 2), (rig.Task().State, retry.Kind, retry.Status, retry.Attempt));
        Assert.Equal((first.Agent, first.Model, first.Effort, first.Methodology, first.Template, first.Round), (retry.Agent, retry.Model, retry.Effort, retry.Methodology, retry.Template, retry.Round));
        Assert.Equal(Pushed, retry.TipBefore);
        var checkout = rig.Runtime.Checkouts[^1].Request;
        Assert.Equal((retry.Id, Pushed), (checkout.StepRunId, checkout.Bundle.Tip));
        Assert.Equal(2, rig.Runtime.Steps.Count);
        Assert.Equal(retry.Deadline, rig.Runtime.Steps[^1].Request.Deadline);
        Assert.Empty(rig.Stores.Github.Comments);
    }

    [Fact]
    public async Task The_work_a_failed_step_left_is_pushed_to_the_Task_branch_before_the_step_is_attempted_again()
    {
        var rig = await RunningAsync();
        rig.Runtime.NextTip = Pushed;

        await FailAsync(rig);

        var (checkpoint, _) = Assert.Single(rig.Runtime.Checkpoints);
        Assert.Equal((StepKind.Implementation, RunRig.StartCommit), (checkpoint.Kind, checkpoint.Parent.Tip));
        var push = Assert.Single(rig.Branches.Pushes).Request;
        Assert.Equal((rig.Task().Branch, Pushed), (push.Branch, push.Bundle.Tip));
        Assert.Equal(StepRunStatus.Started, rig.LastStep().Status);
    }

    [Fact]
    public async Task A_step_that_changed_nothing_before_it_failed_leaves_nothing_to_push_and_is_attempted_again_from_the_same_tip()
    {
        var rig = await RunningAsync();
        rig.Runtime.Unchanged = true;

        await FailAsync(rig);

        Assert.Empty(rig.Branches.Pushes);
        Assert.Equal((2, RunRig.StartCommit, StepRunStatus.Started), (rig.LastStep().Attempt, rig.LastStep().TipBefore, rig.LastStep().Status));
    }

    [Fact]
    public async Task A_step_attempted_again_that_ends_well_goes_on_to_Validation_and_to_the_pull_request()
    {
        var rig = await RunningAsync();
        await FailAsync(rig);

        await rig.ExitAsync(rig.LastStep().Id, 0, StepExitCause.Exited, RunRig.Completed());
        await rig.TickAsync(3);
        await rig.EndValidationAsync();
        await rig.EndValidationAsync();
        await rig.TickAsync(3);

        Assert.Equal(TaskState.PullRequestOpen, rig.Task().State);
        Assert.Equal([1, 2], rig.Steps().Where(step => step.Kind == StepKind.Implementation).Select(step => step.Attempt));
        Assert.All(rig.Steps().Where(step => step.IsValidation), step => Assert.Equal(1, step.Attempt));
        Assert.Single(rig.Pulls.Publications);
        Assert.Empty(rig.Stores.Github.Comments);
    }

    [Fact]
    public async Task A_step_that_fails_in_every_attempt_ends_the_Task_when_the_bound_is_spent_with_its_label_a_comment_and_all_its_work_on_the_branch()
    {
        var rig = await RunningAsync();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            rig.Runtime.NextTip = new string((char)('0' + attempt), 40);
            await FailAsync(rig, output: [RunRig.Completed()]);
        }

        var steps = rig.Steps();
        Assert.Equal([1, 2, 3], steps.Select(step => step.Attempt));
        Assert.All(steps, step => Assert.Equal(("unknown", StepRunStatus.Concluded), (step.Failure, step.Status)));
        Assert.Equal(TaskState.Failed, rig.Task().State);
        Assert.Equal(["1", "2", "3"], rig.Branches.Pushes.Select(push => push.Request.Bundle.Tip[..1]));
        Assert.Contains("hephaestus:failed", rig.Stores.Github.LabelsOf(RunRig.RepositoryId, RunRig.Issue));
        Assert.Equal(
            $"**Failed:** `unknown` in `implementation`.\n\nAttempts: 3. The bound of retries for a step is 2, and it is spent.\n\nNext: nothing until you apply the `hephaestus` label to this issue again. Work so far is on `{rig.Task().Branch}`.",
            CommentOf(rig));
        Assert.Empty(rig.Runtime.Validations);
        Assert.Empty(rig.Pulls.Publications);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(4, 5)]
    public async Task The_bound_is_the_installation_setting_and_a_step_has_that_many_attempts_again_and_one(int retries, int attempts)
    {
        var rig = await RunningAsync(retries);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            Assert.Equal((TaskState.Running, attempt), (rig.Task().State, rig.LastStep().Attempt));
            await FailAsync(rig, output: [RunRig.Completed()]);
        }

        Assert.Equal((TaskState.Failed, attempts), (rig.Task().State, rig.Steps().Count));
        Assert.Contains($"Attempts: {attempts}. The bound of retries for a step is {retries}, and it is spent.", CommentOf(rig), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_usage_limit_the_adapter_cannot_identify_is_recorded_as_unknown_and_attempted_again_and_never_treated_as_one()
    {
        var rig = await RunningAsync();

        await FailAsync(rig, 1, StepExitCause.Exited, "429 You have hit your limit; resets at 5pm");

        Assert.Equal(("unknown", 2), (rig.Steps()[0].Failure, rig.LastStep().Attempt));
        Assert.Equal(TaskState.Running, rig.Task().State);
        Assert.DoesNotContain(rig.Steps(), step => step.Failure == "usage-limit");
    }

    [Fact]
    public async Task A_class_the_adapter_names_that_spends_neither_bound_ends_the_Task_at_once()
    {
        var rig = await RunningAsync();
        rig.Adapter.Class = FailureClass.UsageLimit;

        await FailAsync(rig, 1);

        Assert.Equal((TaskState.Failed, 1), (rig.Task().State, rig.Steps().Count));
        Assert.Contains("**Failed:** `usage-limit` in `implementation`.", CommentOf(rig), StringComparison.Ordinal);
        Assert.Contains("A failure of this class is not tried again.", CommentOf(rig), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_commit_that_cannot_be_bundled_for_a_moment_is_attempted_again_instead_of_failing_the_Task()
    {
        var rig = new RunRig();
        rig.Mirrors.FailNextFetches = 1;
        await rig.QueueAsync();
        await rig.StartAsync();

        await rig.TickAsync();

        var steps = rig.Steps();
        Assert.Equal((TaskState.Running, 2), (rig.Task().State, steps.Count));
        Assert.Equal(("infrastructure-error", StepRunStatus.Concluded), (steps[0].Failure, steps[0].Status));
        Assert.Equal((2, StepRunStatus.Started), (steps[1].Attempt, steps[1].Status));
        Assert.Single(rig.Runtime.Steps);
    }

    [Fact]
    public async Task A_commit_that_can_never_be_bundled_ends_the_Task_when_the_bound_is_spent_and_says_so()
    {
        var rig = new RunRig();
        rig.Mirrors.FetchFails = MirrorFailure.Unreachable;
        await rig.QueueAsync();
        await rig.StartAsync();

        await rig.TickAsync();

        Assert.Equal((TaskState.Failed, 3), (rig.Task().State, rig.Steps().Count));
        Assert.Empty(rig.Runtime.Checkouts);
        Assert.Contains("**Failed:** `infrastructure-error` in `implementation`.", CommentOf(rig), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_Validation_ends_the_Task_without_a_retry_and_the_comment_names_the_command_and_its_exit_status()
    {
        var rig = new RunRig();
        await rig.ImplementAsync();
        await rig.TickAsync();

        await rig.EndValidationAsync(exitCode: 2);
        await rig.TickAsync();

        Assert.Equal(TaskState.Failed, rig.Task().State);
        Assert.Equal(1, rig.Steps().Count(step => step.IsValidation));
        Assert.Equal(
            $"**Failed:** `validation-failure` in `validation`.\n\nCommand: `dotnet build` exited with 2.\nAttempts: 1. A failure of this class is not tried again.\n\nNext: nothing until you apply the `hephaestus` label to this issue again. Work so far is on `{rig.Task().Branch}`.",
            CommentOf(rig));
        Assert.Empty(rig.Pulls.Publications);
    }

    [Fact]
    public async Task A_command_of_Validation_stopped_at_its_deadline_is_attempted_again_on_the_same_tip_and_the_commands_after_it_wait()
    {
        var rig = new RunRig();
        await rig.ImplementAsync();
        await rig.TickAsync();
        var tip = rig.Runtime.NextTip;

        await rig.EndValidationAsync(0, cause: StepExitCause.Timeout);

        var validations = rig.Steps().Where(step => step.IsValidation).ToList();
        Assert.Equal([(0, 1), (0, 2)], validations.Select(step => (step.CommandIndex!.Value, step.Attempt)));
        Assert.Equal(("timeout", StepRunStatus.Concluded), (validations[0].Failure, validations[0].Status));
        Assert.Equal((tip, StepRunStatus.Started), (validations[1].TipBefore, validations[1].Status));
        Assert.Equal(2, rig.Runtime.Validations.Count);
        Assert.Equal(["dotnet build", "dotnet build"], rig.Runtime.Validations.Select(v => v.Request.Command));
        Assert.Equal(TaskState.Running, rig.Task().State);
    }

    [Fact]
    public async Task A_command_that_passes_when_attempted_again_counts_once_and_the_pull_request_is_published()
    {
        var rig = new RunRig();
        await rig.ImplementAsync();
        await rig.TickAsync();
        await rig.EndValidationAsync(0, cause: StepExitCause.Timeout);

        await rig.EndValidationAsync();
        await rig.EndValidationAsync();
        await rig.TickAsync(3);

        Assert.Equal(TaskState.PullRequestOpen, rig.Task().State);
        var request = Assert.Single(rig.Pulls.Publications).Request;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(request.Body, "dotnet build"));
        Assert.Contains("dotnet test", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_command_that_is_stopped_at_its_deadline_every_time_ends_the_Task_when_the_bound_is_spent()
    {
        var rig = new RunRig();
        rig.SetStepRetries(1);
        await rig.ImplementAsync();
        await rig.TickAsync();

        await rig.EndValidationAsync(0, cause: StepExitCause.Timeout);
        await rig.EndValidationAsync(0, cause: StepExitCause.Timeout);

        Assert.Equal(TaskState.Failed, rig.Task().State);
        Assert.Equal(2, rig.Steps().Count(step => step.IsValidation));
        Assert.Contains("**Failed:** `timeout` in `validation`.\n\nCommand: `dotnet build`.\nAttempts: 2. The bound of retries for a step is 1", CommentOf(rig), StringComparison.Ordinal);
    }

    // The deadlines and the Task's duration limit (NFR-5, AR-55).

    [Fact]
    public async Task The_deadline_of_an_Agent_run_is_recorded_before_its_container_starts_and_a_CLI_that_waits_for_a_network_that_is_gone_is_stopped_only_by_the_runtime()
    {
        var rig = await RunningAsync(configuration: LimitedTo("1h"));
        var step = rig.LastStep();

        // The Agent prints nothing and never ends; the loop does not end it, whatever time passes.
        rig.Time.Advance(TimeSpan.FromMinutes(59));
        await rig.TickAsync(3);

        Assert.Equal(step.PlannedAt + TimeSpan.FromHours(1), step.Deadline);
        Assert.Equal(step.Deadline, Assert.Single(rig.Runtime.Steps).Request.Deadline);
        Assert.Equal((StepRunStatus.Started, TaskState.Running), (rig.LastStep().Status, rig.Task().State));

        rig.Time.Advance(TimeSpan.FromMinutes(1));
        await rig.ExitAsync(step.Id, 0, StepExitCause.Timeout);
        await rig.TickAsync(2);

        Assert.Equal((StepRunStatus.Concluded, "timeout", StepExitCause.Timeout), (rig.Steps()[0].Status, rig.Steps()[0].Failure, rig.Steps()[0].Cause));
    }

    [Fact]
    public async Task The_deadline_of_a_command_of_Validation_is_recorded_before_its_container_starts_and_is_its_own_limit()
    {
        var rig = new RunRig();
        await rig.ImplementAsync();

        await rig.TickAsync();

        var command = rig.LastStep();
        Assert.Equal(command.PlannedAt + ValidationRun.DefaultCommandLimit, command.Deadline);
        Assert.Equal(command.Deadline, Assert.Single(rig.Runtime.Validations).Request.Deadline);
    }

    [Fact]
    public async Task A_step_attempted_again_is_stopped_no_later_than_the_moment_the_Tasks_time_in_Running_reaches_its_limit()
    {
        var rig = await RunningAsync(configuration: LimitedTo("1h"));
        rig.Time.Advance(TimeSpan.FromMinutes(40));

        await FailAsync(rig);

        var retry = rig.LastStep();
        Assert.Equal(2, retry.Attempt);
        Assert.Equal(rig.Time.Now + TimeSpan.FromMinutes(20), retry.Deadline);
        Assert.Equal(TimeSpan.FromMinutes(40), rig.Task().TimeInRunning(rig.Time.Now));
        Assert.Equal(retry.Deadline, rig.Runtime.Steps[^1].Request.Deadline);
    }

    [Fact]
    public async Task A_command_of_Validation_is_stopped_no_later_than_the_Tasks_limit_even_when_its_own_limit_is_longer()
    {
        var rig = new RunRig(LimitedTo("1h"));
        await rig.ImplementAsync();
        rig.Time.Advance(TimeSpan.FromMinutes(45));

        await rig.TickAsync();

        var command = rig.LastStep();
        Assert.Equal(rig.Time.Now + TimeSpan.FromMinutes(15), command.Deadline);
    }

    [Fact]
    public async Task A_Task_that_ran_before_is_given_a_first_deadline_that_leaves_out_the_time_it_spent()
    {
        var rig = new RunRig(LimitedTo("1h"));
        var task = await rig.QueueAsync();
        rig.Stores.Tasks[0] = task with { RunningTime = TimeSpan.FromMinutes(45) };
        await rig.StartAsync();

        await rig.TickAsync();

        Assert.Equal(rig.Time.Now + TimeSpan.FromMinutes(15), rig.LastStep().Deadline);
    }

    [Fact]
    public async Task A_Task_whose_time_in_Running_reaches_its_limit_ends_with_a_timeout_a_checkpoint_no_retry_and_a_comment_that_names_the_limit()
    {
        var rig = await RunningAsync(configuration: LimitedTo("1h"));
        rig.Runtime.NextTip = Pushed;
        var step = rig.LastStep();

        // The runtime stops the step at its deadline, which is the moment the Task's time in Running reaches its limit.
        rig.Time.Advance(TimeSpan.FromHours(1));
        await FailAsync(rig, 0, StepExitCause.Timeout, RunRig.Completed());

        var steps = rig.Steps();
        Assert.Equal(step.Id, Assert.Single(steps).Id);
        Assert.Equal(("timeout", StepExitCause.Timeout, StepRunStatus.Concluded, Pushed), (steps[0].Failure, steps[0].Cause, steps[0].Status, steps[0].TipAfter));
        Assert.Equal(Pushed, Assert.Single(rig.Branches.Pushes).Request.Bundle.Tip);
        Assert.Equal(TaskState.Failed, rig.Task().State);
        Assert.Equal(
            $"**Failed:** `timeout` in `implementation`.\n\nAttempts: 1. The Task reached its duration limit of 1h in `running`, so no step is tried again.\n\nNext: nothing until you apply the `hephaestus` label to this issue again. Work so far is on `{rig.Task().Branch}`.",
            CommentOf(rig));
    }

    [Fact]
    public async Task A_step_stopped_a_moment_before_the_end_of_the_Tasks_time_is_read_as_the_end_of_it_because_its_deadline_was_set_a_moment_before_the_clock_of_the_Task_was_read()
    {
        var rig = await RunningAsync(configuration: LimitedTo("1h"));

        rig.Time.Advance(TimeSpan.FromHours(1) - TimeSpan.FromMilliseconds(20));
        await FailAsync(rig, 0, StepExitCause.Timeout);

        Assert.Equal((TaskState.Failed, 1), (rig.Task().State, rig.Steps().Count));
    }

    [Fact]
    public async Task A_Task_with_time_left_whose_step_timed_out_is_attempted_again()
    {
        var rig = await RunningAsync(configuration: LimitedTo("1h"));

        rig.Time.Advance(TimeSpan.FromMinutes(30));
        await FailAsync(rig, 0, StepExitCause.Timeout);

        Assert.Equal((TaskState.Running, 2), (rig.Task().State, rig.LastStep().Attempt));
    }

    [Fact]
    public async Task The_installation_limit_holds_the_Project_limit_down_for_the_Task_duration_too()
    {
        var rig = await RunningAsync(configuration: LimitedTo("3h"));
        rig.Stores.Limits = new InstallationLimits(TimeSpan.FromHours(1), null, null);
        rig.Time.Advance(TimeSpan.FromHours(1));

        await FailAsync(rig, 0, StepExitCause.Timeout);

        Assert.Equal(TaskState.Failed, rig.Task().State);
        Assert.Contains("duration limit of 1h", CommentOf(rig), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_that_failed_after_the_Task_left_Running_is_not_given_a_step_nobody_asked_for()
    {
        var rig = await RunningAsync();
        await rig.ExitAsync(rig.LastStep().Id, 2, StepExitCause.Exited, RunRig.Completed());
        await rig.TickAsync();
        // The Owner's reply takes the Task out of Running while its failed step waits for its push.
        rig.Stores.Tasks[0] = rig.Stores.Tasks[0] with { State = TaskState.Queued };

        await rig.TickAsync(2);

        Assert.Single(rig.Steps());
        Assert.Equal(TaskState.Queued, rig.Task().State);
    }

    [Fact]
    public async Task What_a_failed_Task_says_on_its_issue_is_one_comment_whose_marker_is_the_effects_key_and_is_not_posted_twice()
    {
        var rig = new RunRig();
        rig.NoRetries();
        await rig.ImplementAsync();
        await rig.TickAsync();
        await rig.EndValidationAsync(exitCode: 2);

        await rig.TickAsync(3);

        var (_, _, _, key) = Assert.Single(rig.Stores.Github.Comments);
        var effect = Assert.Single(rig.Stores.Journal, entry => entry.Entry == JournalEntryKind.Intent && entry.Effect.Kind == EffectKind.Comment).Effect;
        Assert.Equal((effect.Key, TaskFailureNotice.Detail), (key, effect.Detail));
        Assert.Contains(rig.Stores.Journal, entry => entry.Entry == JournalEntryKind.Result && entry.Effect.Key == key && entry.Result == EffectResult.Applied);
    }

    // The work of a step that failed is kept as far as it can be, and never at the cost of holding the slot for ever.

    [Fact]
    public async Task Work_that_cannot_be_kept_is_tried_again_by_each_tick_up_to_the_bound_and_then_left_behind_and_said_so_and_the_step_is_decided_without_it()
    {
        var rig = await RunningAsync();
        rig.Runtime.Throws[nameof(FakeRuntime.CheckpointAsync)] = new WorkspaceRuntimeException(new Hephaestus.Core.Localization.Message("workspace.probe.docker"));
        await rig.ExitAsync(rig.LastStep().Id, 2, StepExitCause.Exited, RunRig.Completed());

        for (var attempt = 1; attempt < Hephaestus.Core.Loop.OrchestratorLoop.MaximumStartAttempts; attempt++)
        {
            var waiting = await rig.TickAsync();
            Assert.Single(rig.Steps());
            Assert.DoesNotContain(waiting.TaskProblems!, line => line.Contains("left behind", StringComparison.Ordinal));
        }

        var last = await rig.TickAsync();

        Assert.Contains(last.TaskProblems!, line => line.Contains("could not be kept and is left behind", StringComparison.Ordinal));
        Assert.Equal((2, TaskState.Running, "unknown"), (rig.Steps().Count, rig.Task().State, rig.Steps()[0].Failure));
        Assert.Empty(rig.Branches.Pushes);
    }

    [Fact]
    public async Task Work_whose_start_commit_cannot_be_bundled_is_left_behind_in_the_same_way()
    {
        var rig = await RunningAsync();
        rig.Mirrors.FetchFails = MirrorFailure.Unreachable;
        await rig.ExitAsync(rig.LastStep().Id, 2, StepExitCause.Exited, RunRig.Completed());

        var reports = new List<Hephaestus.Core.Loop.TickReport>();
        for (var tick = 0; tick < Hephaestus.Core.Loop.OrchestratorLoop.MaximumStartAttempts; tick++)
        {
            reports.Add(await rig.TickAsync());
        }

        Assert.Contains(reports[^1].TaskProblems!, line => line.Contains("could not be bundled", StringComparison.Ordinal));
        Assert.Empty(rig.Runtime.Checkpoints);
        Assert.Equal("unknown", rig.Steps()[0].Failure);
    }

    [Fact]
    public async Task When_GitHub_is_not_connected_nothing_is_committed_and_the_failed_step_is_decided_on_at_once()
    {
        var rig = await RunningAsync();
        rig.Stores.AppSlug = null;

        await FailAsync(rig, 2, StepExitCause.Exited, RunRig.Completed());

        Assert.Empty(rig.Runtime.Checkpoints);
        Assert.Equal((2, TaskState.Running, StepRunStatus.Started), (rig.Steps().Count, rig.Task().State, rig.LastStep().Status));
    }

    [Fact]
    public async Task A_step_that_ended_well_and_cannot_be_committed_because_GitHub_is_not_connected_is_an_infrastructure_error_that_is_attempted_again()
    {
        var rig = await RunningAsync();
        rig.Stores.AppSlug = null;

        await rig.ExitAsync(rig.LastStep().Id, 0, StepExitCause.Exited, RunRig.Completed());
        var report = await rig.TickAsync();

        Assert.Equal(("infrastructure-error", StepRunStatus.Concluded, "completed"), (rig.Steps()[0].Failure, rig.Steps()[0].Status, rig.Steps()[0].Outcome));
        Assert.Contains(report.TaskProblems!, line => line.Contains("GitHub is not connected", StringComparison.Ordinal));
        Assert.Equal(2, rig.LastStep().Attempt);
    }

    [Fact]
    public async Task A_step_that_ended_well_whose_start_commit_cannot_be_bundled_is_tried_again_by_each_tick_and_then_an_infrastructure_error_that_is_attempted_again()
    {
        var rig = await RunningAsync();
        rig.Mirrors.BundleFails = MirrorFailure.CommitMissing;

        await rig.ExitAsync(rig.LastStep().Id, 0, StepExitCause.Exited, RunRig.Completed());
        await rig.TickAsync(Hephaestus.Core.Loop.OrchestratorLoop.MaximumStartAttempts - 1);
        Assert.Equal((StepRunStatus.Ended, null), (rig.Steps()[0].Status, rig.Steps()[0].Failure));
        await rig.TickAsync();

        Assert.Equal(("infrastructure-error", "completed", StepRunStatus.Concluded), (rig.Steps()[0].Failure, rig.Steps()[0].Outcome, rig.Steps()[0].Status));
        Assert.Empty(rig.Runtime.Checkpoints);
    }

    [Fact]
    public async Task A_failure_recorded_under_a_name_this_version_does_not_know_is_unknown_and_is_attempted_again()
    {
        var rig = await RunningAsync();
        rig.Stores.StepRuns[0] = rig.Stores.StepRuns[0] with { Status = StepRunStatus.Ended, StartedAt = null, Failure = "no-such-class" };

        await rig.TickAsync();

        Assert.Equal((2, 2), (rig.Steps().Count, rig.LastStep().Attempt));
    }

    [Fact]
    public async Task A_step_that_failed_and_waits_for_its_push_is_not_decided_on_before_the_push_is_made()
    {
        var rig = await RunningAsync();
        rig.Branches.Throws = new IOException("GitHub is gone");
        await rig.ExitAsync(rig.LastStep().Id, 2, StepExitCause.Exited, RunRig.Completed());

        await rig.TickAsync(3);

        Assert.Equal((StepRunStatus.Checkpointed, 1, TaskState.Running), (rig.Steps()[0].Status, rig.Steps().Count, rig.Task().State));
        Assert.Empty(rig.Runtime.Validations);
    }

    [Fact]
    public async Task A_result_the_Implementation_stored_that_cannot_be_read_back_at_the_end_of_Validation_is_an_infrastructure_error_that_is_bounded_like_any_other()
    {
        var rig = new RunRig();
        await rig.ImplementAsync();
        await rig.TickAsync();
        await rig.EndValidationAsync();
        rig.Stores.StepRuns[0] = rig.Stores.StepRuns[0] with { Result = "garbled" };

        for (var round = 0; round < 6 && rig.Task().State == TaskState.Running; round++)
        {
            await rig.EndValidationAsync();
        }

        Assert.Equal(TaskState.Failed, rig.Task().State);
        Assert.Contains("**Failed:** `infrastructure-error` in `validation`.", CommentOf(rig), StringComparison.Ordinal);
        Assert.Empty(rig.Pulls.Publications);
    }

    [Fact]
    public async Task A_step_that_ended_with_a_failure_and_never_started_has_no_work_to_keep()
    {
        var rig = new RunRig();
        rig.Mirrors.FailNextFetches = 1;
        await rig.QueueAsync();
        await rig.StartAsync();

        await rig.TickAsync();

        Assert.Empty(rig.Runtime.Checkpoints);
        Assert.Null(rig.Steps()[0].StartedAt);
    }
}
