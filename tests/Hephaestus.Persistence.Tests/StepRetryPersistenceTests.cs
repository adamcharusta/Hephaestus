using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>
/// Story 3.16, AD-11, NFR-10, NFR-5: the attempt a step run is, the retry that is planned in the transaction that concludes the failed step and only for a Task that still runs, and the time a Task has spent in <c>running</c>, which the database keeps
/// where the Task changes state.
/// </summary>
public class StepRetryPersistenceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Commit = new('a', 40);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = now;

        public void Advance(TimeSpan by) => Now += by;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Rig : IDisposable
    {
        private long _sequence;

        public Rig()
        {
            Installation = new TempInstallation(Clock);
        }

        public MovingClock Clock { get; } = new(Noon);

        public TempInstallation Installation { get; }

        public IUnitOfWork Work => Installation.Provider.GetRequiredService<IUnitOfWork>();

        public IReadStore Reads => Installation.Reads;

        public async Task<TaskRecord> QueueAsync()
        {
            await Installation.MigrateAsync();
            var task = new TaskRecord(EntityId.New(Clock), 101, 7, TaskState.Queued, "hephaestus/7-a-task", Noon, Noon);
            var snapshot = new IssueSnapshot("A task", "Details.", IssueContent.Hash("A task", "Details."), 1, Noon);
            await Installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync([new Signal(Signal.GitHubSource, "issue-event:7", SignalKind.Labeled, 101, 7, 29, "alice", "hephaestus", null, null, Noon, "A task", "Details.")], Token);
            await Work.ApplyAsync(new SignalApplication(++_sequence, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, snapshot)), Token);
            return task;
        }

        /// <summary>Moves the Task the way a Signal does: the state, in the transaction that marks the Signal.</summary>
        public async Task MoveAsync(Guid taskId, TaskState to)
        {
            var id = await Installation.Provider.GetRequiredService<ISignalInbox>().AppendOneAsync(Signal.Start(101, Guid.NewGuid(), Clock.Now), Token);
            Assert.True(await Work.ApplyAsync(new SignalApplication(id, SignalStatus.Applied, TaskId: taskId, MoveTaskTo: to), Token));
        }

        public async Task<TaskRecord> TaskAsync(Guid id) => (await Reads.FindTaskAsync(id, Token))!;

        public void Dispose() => Installation.Dispose();
    }

    private static NewStepRun Plan(Guid taskId, Rig rig, int attempt = 1, StepKind kind = StepKind.Implementation) =>
        new(EntityId.New(rig.Clock), taskId, kind, 0, rig.Clock.Now.AddHours(2), rig.Clock.Now, Commit, Agent.ClaudeCode, "claude-opus-5-5", Effort.High, "default", "implementation.md@sha256:abc", kind == StepKind.Validation ? 0 : null, attempt);

    private static TaskDispatch Dispatch(Guid taskId, NewStepRun step) =>
        new(taskId, null, step, [new NewEffect(taskId, "s1", EffectKind.Label, "running")]);

    private static async Task<(Rig Rig, TaskRecord Task, NewStepRun Step)> RunningAsync()
    {
        var rig = new Rig();
        var task = await rig.QueueAsync();
        await rig.Work.DispatchAsync(new TaskDispatch(task.Id, DispatchSnapshot(task.Id), Plan(task.Id, rig), []), Token);
        var step = (await rig.Reads.ListStepRunsAsync(task.Id, Token)).Single();
        return (rig, task, Plan(task.Id, rig, 2) with { TipBefore = step.TipBefore });
    }

    private static TaskSnapshot DispatchSnapshot(Guid taskId) => new(
        taskId,
        Commit,
        new Hephaestus.Core.Projects.ProjectConfiguration("opus", null, "deep", "default", ["dotnet build"], 2, "ubuntu:24.04", Hephaestus.Core.Projects.ProjectLimits.None, [], null),
        new ResolvedImage("ubuntu:24.04", "sha256:" + new string('1', 64), Hephaestus.Core.AgentBinaries.AgentArchitecture.X64, CLibrary.Glibc),
        Noon);

    private static StepRunEnd Failed(Rig rig) => new(rig.Clock.Now, 2, StepExitCause.Exited, FailureClass.Unknown, null, null);

    [Fact]
    public async Task A_step_run_has_its_attempt_which_is_one_unless_it_attempts_a_failed_step_again_and_it_is_read_back()
    {
        var (rig, task, retry) = await RunningAsync();
        using var _ = rig;
        var first = (await rig.Reads.ListStepRunsAsync(task.Id, Token)).Single();
        await rig.Work.ChangeStepRunAsync(new StepRunChange(first.Id, StepRunStatus.Ended, Failed(rig)), Token);

        Assert.True(await rig.Work.ChangeStepRunAsync(new StepRunChange(first.Id, StepRunStatus.Concluded, Failure: FailureClass.Unknown, Retry: retry), Token));

        var steps = await rig.Reads.ListStepRunsAsync(task.Id, Token);
        Assert.Equal([(1, StepRunStatus.Concluded, "unknown"), (2, StepRunStatus.Planned, null)], steps.Select(step => (step.Attempt, step.Status, step.Failure)));
        Assert.Equal([1, 2], steps.Select(step => step.Sequence));
        Assert.Equal((retry.Id, retry.Deadline, retry.TipBefore, "claude-opus-5-5"), (steps[1].Id, steps[1].Deadline, steps[1].TipBefore, steps[1].Model));
    }

    [Fact]
    public async Task The_retry_is_planned_in_the_transaction_of_the_conclusion_and_a_conclusion_recorded_twice_plans_it_once()
    {
        var (rig, task, retry) = await RunningAsync();
        using var _ = rig;
        var first = (await rig.Reads.ListStepRunsAsync(task.Id, Token)).Single();
        await rig.Work.ChangeStepRunAsync(new StepRunChange(first.Id, StepRunStatus.Ended, Failed(rig)), Token);
        var conclusion = new StepRunChange(first.Id, StepRunStatus.Concluded, Failure: FailureClass.Unknown, Retry: retry);

        Assert.True(await rig.Work.ChangeStepRunAsync(conclusion, Token));
        Assert.False(await rig.Work.ChangeStepRunAsync(conclusion with { Retry = Plan(task.Id, rig, 3) }, Token));

        Assert.Equal(2, (await rig.Reads.ListStepRunsAsync(task.Id, Token)).Count);
    }

    [Fact]
    public async Task A_conclusion_that_ends_the_Task_plans_no_retry_even_when_one_is_given()
    {
        var (rig, task, retry) = await RunningAsync();
        using var _ = rig;
        var first = (await rig.Reads.ListStepRunsAsync(task.Id, Token)).Single();
        await rig.Work.ChangeStepRunAsync(new StepRunChange(first.Id, StepRunStatus.Ended, Failed(rig)), Token);
        var move = new TaskMove(TaskState.Running, TaskState.Failed, [new NewEffect(task.Id, EffectKeys.StepCause(first.Id), EffectKind.Label, "failed")]);

        Assert.True(await rig.Work.ChangeStepRunAsync(new StepRunChange(first.Id, StepRunStatus.Concluded, Move: move, Failure: FailureClass.Unknown, Retry: retry), Token));

        Assert.Equal(TaskState.Failed, (await rig.TaskAsync(task.Id)).State);
        Assert.Single(await rig.Reads.ListStepRunsAsync(task.Id, Token));
    }

    [Fact]
    public async Task A_Task_that_left_Running_is_not_given_the_retry_of_a_step_that_concludes_after_it()
    {
        var (rig, task, retry) = await RunningAsync();
        using var _ = rig;
        var first = (await rig.Reads.ListStepRunsAsync(task.Id, Token)).Single();
        await rig.Work.ChangeStepRunAsync(new StepRunChange(first.Id, StepRunStatus.Ended, Failed(rig)), Token);
        await rig.MoveAsync(task.Id, TaskState.Queued);

        Assert.True(await rig.Work.ChangeStepRunAsync(new StepRunChange(first.Id, StepRunStatus.Concluded, Failure: FailureClass.Unknown, Retry: retry), Token));

        Assert.Single(await rig.Reads.ListStepRunsAsync(task.Id, Token));
    }

    [Fact]
    public async Task The_time_a_Task_spends_in_running_is_added_by_the_database_whenever_the_Task_leaves_the_state_and_a_stretch_begins_when_it_enters_it()
    {
        using var rig = new Rig();
        var task = await rig.QueueAsync();
        Assert.Equal((TimeSpan.Zero, (DateTimeOffset?)null), ((await rig.TaskAsync(task.Id)).RunningTime, (await rig.TaskAsync(task.Id)).RunningSince));

        await rig.Work.DispatchAsync(new TaskDispatch(task.Id, DispatchSnapshot(task.Id), Plan(task.Id, rig), []), Token);
        var running = await rig.TaskAsync(task.Id);
        Assert.Equal((TimeSpan.Zero, (DateTimeOffset?)Noon), (running.RunningTime, running.RunningSince));
        Assert.Equal(TimeSpan.FromMinutes(7), running.TimeInRunning(Noon.AddMinutes(7)));

        rig.Clock.Advance(TimeSpan.FromMinutes(25));
        await rig.MoveAsync(task.Id, TaskState.WaitingForOwner);
        var waiting = await rig.TaskAsync(task.Id);
        Assert.Equal((TimeSpan.FromMinutes(25), (DateTimeOffset?)null), (waiting.RunningTime, waiting.RunningSince));
        Assert.Equal(TimeSpan.FromMinutes(25), waiting.TimeInRunning(Noon.AddDays(3)));

        rig.Clock.Advance(TimeSpan.FromHours(5));
        await rig.MoveAsync(task.Id, TaskState.Queued);
        Assert.Equal(TimeSpan.FromMinutes(25), (await rig.TaskAsync(task.Id)).RunningTime);

        rig.Clock.Advance(TimeSpan.FromHours(1));
        await rig.Work.DispatchAsync(new TaskDispatch(task.Id, null, Plan(task.Id, rig), []), Token);
        Assert.Equal(rig.Clock.Now, (await rig.TaskAsync(task.Id)).RunningSince);

        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        await rig.MoveAsync(task.Id, TaskState.PullRequestOpen);
        var done = await rig.TaskAsync(task.Id);
        Assert.Equal((TimeSpan.FromMinutes(35), (DateTimeOffset?)null), (done.RunningTime, done.RunningSince));
    }

    [Fact]
    public async Task The_time_is_added_whichever_write_takes_the_Task_out_of_running_and_the_precision_is_that_of_the_clock()
    {
        var (rig, task, _) = await RunningAsync();
        using var _ = rig;
        var step = (await rig.Reads.ListStepRunsAsync(task.Id, Token)).Single();
        await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, Failed(rig)), Token);
        rig.Clock.Advance(TimeSpan.FromMinutes(90) + TimeSpan.FromMilliseconds(123));

        var move = new TaskMove(TaskState.Running, TaskState.Failed, [new NewEffect(task.Id, EffectKeys.StepCause(step.Id), EffectKind.Label, "failed")]);
        await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Concluded, Move: move, Failure: FailureClass.Unknown), Token);

        Assert.Equal(TimeSpan.FromMinutes(90) + TimeSpan.FromMilliseconds(123), (await rig.TaskAsync(task.Id)).RunningTime);
    }

    [Fact]
    public async Task A_Task_that_failed_and_is_opted_in_again_begins_with_no_time_spent_and_one_that_waited_keeps_what_it_spent()
    {
        var (rig, task, _) = await RunningAsync();
        using var _ = rig;
        rig.Clock.Advance(TimeSpan.FromMinutes(40));
        await rig.MoveAsync(task.Id, TaskState.Failed);
        Assert.Equal(TimeSpan.FromMinutes(40), (await rig.TaskAsync(task.Id)).RunningTime);

        await rig.MoveAsync(task.Id, TaskState.Queued);

        var queued = await rig.TaskAsync(task.Id);
        Assert.Equal((TimeSpan.Zero, (DateTimeOffset?)null), (queued.RunningTime, queued.RunningSince));
    }

    [Fact]
    public async Task A_write_that_does_not_change_the_state_leaves_the_time_as_it_is()
    {
        var (rig, task, _) = await RunningAsync();
        using var _ = rig;
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        var step = (await rig.Reads.ListStepRunsAsync(task.Id, Token)).Single();

        await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Started), Token);
        await rig.MoveAsync(task.Id, TaskState.Running);

        var after = await rig.TaskAsync(task.Id);
        Assert.Equal((TimeSpan.Zero, (DateTimeOffset?)Noon), (after.RunningTime, after.RunningSince));
    }

    [Fact]
    public async Task A_raised_Escalation_adds_the_time_too_because_the_trigger_is_the_databases_and_not_one_writes_path()
    {
        var (rig, task, _) = await RunningAsync();
        using var _ = rig;
        rig.Clock.Advance(TimeSpan.FromMinutes(12));
        var escalation = new Escalation(EntityId.New(rig.Clock), task.Id, EscalationKind.Decision, "Which database?", ["Postgres"], ResumeConditions.TrustedReply, "s1", rig.Clock.Now);
        var plan = new EscalationPlan(escalation, TaskState.Running, []);

        Assert.True(await rig.Work.EscalateAsync(plan, Token));

        Assert.Equal(TimeSpan.FromMinutes(12), (await rig.TaskAsync(task.Id)).RunningTime);
    }

    [Fact]
    public async Task A_Task_that_is_running_when_the_database_is_upgraded_counts_from_its_last_update_and_a_step_run_it_had_is_the_first_attempt()
    {
        using var rig = new Rig();
        Directory.CreateDirectory(rig.Installation.Options.DataDirectory);
        await using (var context = rig.Installation.Database.OpenWriter())
        {
            await context.GetService<IMigrator>().MigrateAsync("20261128100000_AddDispatch", Token);
            var id = Guid.CreateVersion7().ToString("D");
            var step = Guid.CreateVersion7().ToString("D");
            var at = "2026-11-28T11:00:00.0000000Z";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "Tasks" ("Id", "RepositoryId", "IssueNumber", "State", "Branch", "CreatedAt", "UpdatedAt") VALUES ({id}, 101, 7, 'running', 'hephaestus/7-a-task', {at}, {at})""",
                Token);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "StepRuns" ("Id", "TaskId", "Sequence", "Kind", "Round", "Status", "Deadline", "PlannedAt", "TipBefore") VALUES ({step}, {id}, 1, 'implementation', 0, 'planned', {at}, {at}, {Commit})""",
                Token);
        }

        await rig.Installation.MigrateAsync();

        var task = (await rig.Reads.ListTasksAsync(TaskState.Running, Token)).Single();
        Assert.Equal((TimeSpan.Zero, (DateTimeOffset?)new DateTimeOffset(2026, 11, 28, 11, 0, 0, TimeSpan.Zero)), (task.RunningTime, task.RunningSince));
        Assert.Equal(1, (await rig.Reads.ListStepRunsAsync(task.Id, Token)).Single().Attempt);
    }
}
