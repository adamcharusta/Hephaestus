using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Windows;
using Hephaestus.Core.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>
/// Story 3.14, AD-1, AD-16, FR-14, FR-16, FR-2: windows, snapshots, and step runs are written only by the one unit of work, in the transaction of what causes them; the database itself refuses a second open window of a Project and a second Task
/// that runs; a change of a step run is made once; and what was written is read back whole.
/// </summary>
public class DispatchPersistenceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly FixedTimeProvider Clock = new(Noon);
    private static readonly string Commit = new('a', 40);
    private static readonly string Pushed = new('b', 40);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Rig : IDisposable
    {
        private long _sequence;

        public Rig()
        {
            Installation = new TempInstallation(Clock);
        }

        public TempInstallation Installation { get; }

        public IUnitOfWork Work => Installation.Provider.GetRequiredService<IUnitOfWork>();

        public ISignalInbox Inbox => Installation.Provider.GetRequiredService<ISignalInbox>();

        public IReadStore Reads => Installation.Reads;

        public async Task<TaskRecord> QueueAsync(int issue = 7, long repository = 101)
        {
            var task = new TaskRecord(EntityId.New(Clock), repository, issue, TaskState.Queued, $"hephaestus/{issue}-a-task", Noon, Noon);
            var snapshot = new IssueSnapshot("A task", "Details.", IssueContent.Hash("A task", "Details."), 1, Noon);
            await Inbox.AppendAsync([new Signal(Signal.GitHubSource, $"issue-event:{issue}:{repository}", SignalKind.Labeled, repository, issue, 29, "alice", "hephaestus", null, null, Noon, "A task", "Details.")], Token);
            await Work.ApplyAsync(new SignalApplication(++_sequence, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, snapshot)), Token);
            return task;
        }

        public async Task<long> RequestAsync(Signal signal)
        {
            var id = await Inbox.AppendOneAsync(signal, Token);
            _sequence = Math.Max(_sequence, id);
            return id;
        }

        public void Dispose() => Installation.Dispose();
    }

    private static async Task<Rig> StartedAsync()
    {
        var rig = new Rig();
        await rig.Installation.MigrateAsync();
        return rig;
    }

    private static TaskSnapshot Snapshot(Guid taskId) => new(
        taskId,
        Commit,
        new ProjectConfiguration("opus", "gpt", "deep", "bmad", ["dotnet build", "dotnet test"], 3, "mcr.microsoft.com/dotnet/sdk:10.0", new ProjectLimits(TimeSpan.FromMinutes(45), 4000, 8L * 1024 * 1024 * 1024), ["nuget.org", "api.nuget.org"], Language.Polish),
        new ResolvedImage("mcr.microsoft.com/dotnet/sdk:10.0", "sha256:" + new string('9', 64), AgentArchitecture.Arm64, CLibrary.Musl),
        Noon);

    /// <summary>The settings hold lists, which a record compares by reference, so a snapshot read back is compared setting by setting.</summary>
    private static void AssertSame(TaskSnapshot expected, TaskSnapshot? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal((expected.TaskId, expected.CommitId, expected.Image, expected.TakenAt), (actual.TaskId, actual.CommitId, actual.Image, actual.TakenAt));
        var (wanted, found) = (expected.Configuration, actual.Configuration);
        Assert.Equal((wanted.PrimaryAlias, wanted.FallbackAlias, wanted.Effort, wanted.Methodology, wanted.CorrectionRounds, wanted.Image, wanted.Limits, wanted.Language), (found.PrimaryAlias, found.FallbackAlias, found.Effort, found.Methodology, found.CorrectionRounds, found.Image, found.Limits, found.Language));
        Assert.Equal(wanted.Validation, found.Validation);
        Assert.Equal(wanted.AllowedEgressHosts, found.AllowedEgressHosts);
    }

    private static NewStepRun Plan(Guid taskId, StepKind kind = StepKind.Implementation, int? command = null) =>
        new(EntityId.New(Clock), taskId, kind, 0, Noon.AddHours(2), Noon, Commit, kind == StepKind.Validation ? null : Agent.ClaudeCode, kind == StepKind.Validation ? null : "claude-opus-5-5", kind == StepKind.Validation ? null : Effort.High, "default", "implementation.md@sha256:abc", command);

    private static TaskDispatch Dispatch(Guid taskId, NewStepRun? step = null, TaskSnapshot? snapshot = null) =>
        new(taskId, snapshot ?? Snapshot(taskId), step ?? Plan(taskId), [new NewEffect(taskId, "s1", EffectKind.Label, "running")]);

    private static StepRunEnd Ended(int exit = 0, FailureClass? failure = null, string? outcome = "completed", string? result = "{}") => new(Noon.AddMinutes(5), exit, StepExitCause.Exited, failure, outcome, result);

    [Fact]
    public async Task A_Signal_that_opens_a_window_writes_it_in_the_same_transaction_and_it_is_read_as_open()
    {
        using var rig = await StartedAsync();
        var id = await rig.RequestAsync(Signal.Start(101, Guid.NewGuid(), Noon));
        var window = new Window(EntityId.New(Clock), 101, Noon);

        Assert.True(await rig.Work.ApplyAsync(new SignalApplication(id, SignalStatus.Applied, OpenWindow: window), Token));

        Assert.Equal(window, Assert.Single(await rig.Reads.ListWindowsAsync(openOnly: true, Token)));
        Assert.Equal(SignalStatus.Applied, (await rig.Inbox.FindAsync(id, Token))!.Status);
    }

    [Fact]
    public async Task The_database_refuses_a_second_open_window_of_a_Project_and_the_Signal_that_would_open_it_stays_pending()
    {
        using var rig = await StartedAsync();
        await rig.Work.ApplyAsync(new SignalApplication(await rig.RequestAsync(Signal.Start(101, Guid.NewGuid(), Noon)), SignalStatus.Applied, OpenWindow: new Window(EntityId.New(Clock), 101, Noon)), Token);
        var second = await rig.RequestAsync(Signal.Start(101, Guid.NewGuid(), Noon));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => rig.Work.ApplyAsync(new SignalApplication(second, SignalStatus.Applied, OpenWindow: new Window(EntityId.New(Clock), 101, Noon)), Token));

        Assert.Single(await rig.Reads.ListWindowsAsync(openOnly: false, Token));
        Assert.Equal(SignalStatus.Pending, (await rig.Inbox.FindAsync(second, Token))!.Status);
    }

    [Fact]
    public async Task A_window_is_closed_with_its_reason_by_a_Signal_or_by_the_loop_and_only_once()
    {
        using var rig = await StartedAsync();
        var first = new Window(EntityId.New(Clock), 101, Noon);
        var second = new Window(EntityId.New(Clock), 202, Noon);
        await rig.Work.ApplyAsync(new SignalApplication(await rig.RequestAsync(Signal.Start(101, Guid.NewGuid(), Noon)), SignalStatus.Applied, OpenWindow: first), Token);
        await rig.Work.ApplyAsync(new SignalApplication(await rig.RequestAsync(Signal.Start(202, Guid.NewGuid(), Noon)), SignalStatus.Applied, OpenWindow: second), Token);

        await rig.Work.ApplyAsync(new SignalApplication(await rig.RequestAsync(Signal.Stop(101, Guid.NewGuid(), Noon)), SignalStatus.Applied, CloseWindow: new WindowClosing(first.Id, WindowEnd.Stopped)), Token);
        var drained = await rig.Work.CloseWindowAsync(new WindowClosing(second.Id, WindowEnd.Drained), Token);
        var again = await rig.Work.CloseWindowAsync(new WindowClosing(second.Id, WindowEnd.Stopped), Token);

        Assert.True(drained);
        Assert.False(again);
        var windows = await rig.Reads.ListWindowsAsync(openOnly: false, Token);
        Assert.Equal(WindowEnd.Stopped, windows.Single(w => w.RepositoryId == 101).End);
        Assert.Equal(WindowEnd.Drained, windows.Single(w => w.RepositoryId == 202).End);
        Assert.All(windows, w => Assert.Equal(Noon, w.ClosedAt));
        Assert.Empty(await rig.Reads.ListWindowsAsync(openOnly: true, Token));
    }

    [Fact]
    public async Task A_Project_whose_window_closed_can_have_another_opened()
    {
        using var rig = await StartedAsync();
        var first = new Window(EntityId.New(Clock), 101, Noon);
        await rig.Work.ApplyAsync(new SignalApplication(await rig.RequestAsync(Signal.Start(101, Guid.NewGuid(), Noon)), SignalStatus.Applied, OpenWindow: first), Token);
        await rig.Work.CloseWindowAsync(new WindowClosing(first.Id, WindowEnd.Drained), Token);

        await rig.Work.ApplyAsync(new SignalApplication(await rig.RequestAsync(Signal.Start(101, Guid.NewGuid(), Noon)), SignalStatus.Applied, OpenWindow: new Window(EntityId.New(Clock), 101, Noon)), Token);

        Assert.Equal(2, (await rig.Reads.ListWindowsAsync(openOnly: false, Token)).Count);
        Assert.Single(await rig.Reads.ListWindowsAsync(openOnly: true, Token));
    }

    [Fact]
    public async Task Starting_a_Task_moves_it_stores_its_snapshot_plans_its_first_step_and_journals_the_label_in_one_transaction()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        var step = Plan(task.Id);

        Assert.True(await rig.Work.DispatchAsync(Dispatch(task.Id, step), Token));

        Assert.Equal(TaskState.Running, (await rig.Reads.FindTaskAsync(task.Id, Token))!.State);
        AssertSame(Snapshot(task.Id), await rig.Reads.FindTaskSnapshotAsync(task.Id, Token));
        var stored = Assert.Single(await rig.Reads.ListStepRunsAsync(task.Id, Token));
        Assert.Equal((step.Id, 1, StepKind.Implementation, StepRunStatus.Planned, Commit), (stored.Id, stored.Sequence, stored.Kind, stored.Status, stored.TipBefore));
        Assert.Equal((Agent.ClaudeCode, "claude-opus-5-5", Effort.High, "implementation.md@sha256:abc"), (stored.Agent!.Value, stored.Model, stored.Effort!.Value, stored.Template));
        Assert.Equal((Noon.AddHours(2), Noon), (stored.Deadline, stored.PlannedAt));
        Assert.Contains(await rig.Reads.ListUnfinishedEffectsAsync(Token), pending => pending.Effect.Kind == EffectKind.Label && pending.Effect.Detail == "running" && pending.Effect.TaskId == task.Id);
        Assert.Equal(stored, await rig.Reads.FindStepRunAsync(step.Id, Token));
    }

    [Fact]
    public async Task A_Task_that_is_not_Queued_is_not_started_and_nothing_is_written_for_the_attempt()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        await rig.Work.DispatchAsync(Dispatch(task.Id), Token);

        var again = await rig.Work.DispatchAsync(Dispatch(task.Id), Token);

        Assert.False(again);
        Assert.Single(await rig.Reads.ListStepRunsAsync(task.Id, Token));
        Assert.Single(await rig.Reads.ListUnfinishedEffectsAsync(Token), pending => pending.Effect.Detail == "running");
    }

    [Fact]
    public async Task While_a_Task_runs_no_other_Task_is_started_by_the_store_and_the_database_refuses_it_even_when_asked_directly()
    {
        using var rig = await StartedAsync();
        var first = await rig.QueueAsync(7);
        var second = await rig.QueueAsync(8);
        await rig.Work.DispatchAsync(Dispatch(first.Id), Token);

        var refused = await rig.Work.DispatchAsync(Dispatch(second.Id), Token);

        Assert.False(refused);
        Assert.Equal(TaskState.Queued, (await rig.Reads.FindTaskAsync(second.Id, Token))!.State);
        Assert.Empty(await rig.Reads.ListStepRunsAsync(second.Id, Token));
        Assert.Null(await rig.Reads.FindTaskSnapshotAsync(second.Id, Token));
        await using var context = rig.Installation.Database.OpenWriter();
        var id = EntityIdConverterText(second.Id);
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Tasks" SET "State" = 'running' WHERE "Id" = {id}""", Token));
    }

    private static string EntityIdConverterText(Guid id) => id.ToString("D");

    [Fact]
    public async Task A_Task_that_starts_again_keeps_the_snapshot_it_has()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        await rig.Work.DispatchAsync(Dispatch(task.Id), Token);
        await rig.Work.ChangeStepRunAsync(new StepRunChange(Plan(task.Id).Id, StepRunStatus.Started), Token);
        await using (var context = rig.Installation.Database.OpenWriter())
        {
            var id = task.Id.ToString("D");
            await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Tasks" SET "State" = 'queued' WHERE "Id" = {id}""", Token);
        }

        Assert.True(await rig.Work.DispatchAsync(new TaskDispatch(task.Id, null, Plan(task.Id), []), Token));

        Assert.Equal(2, (await rig.Reads.ListStepRunsAsync(task.Id, Token)).Count);
        AssertSame(Snapshot(task.Id), await rig.Reads.FindTaskSnapshotAsync(task.Id, Token));
    }

    [Fact]
    public async Task A_further_step_is_planned_only_for_a_Task_that_runs_and_takes_the_next_sequence()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        var early = Plan(task.Id, StepKind.Validation, 0);
        Assert.False(await rig.Work.PlanStepRunAsync(early, Token));
        await rig.Work.DispatchAsync(Dispatch(task.Id), Token);

        Assert.True(await rig.Work.PlanStepRunAsync(early, Token));
        Assert.False(await rig.Work.PlanStepRunAsync(early, Token));
        Assert.True(await rig.Work.PlanStepRunAsync(Plan(task.Id, StepKind.Validation, 1), Token));

        var steps = await rig.Reads.ListStepRunsAsync(task.Id, Token);
        Assert.Equal([1, 2, 3], steps.Select(s => s.Sequence));
        Assert.Equal([null, 0, 1], steps.Select(s => s.CommandIndex));
        Assert.Equal([StepKind.Implementation, StepKind.Validation, StepKind.Validation], steps.Select(s => s.Kind));
        Assert.Null(steps[1].Agent);
    }

    [Fact]
    public async Task A_step_run_goes_forward_one_status_at_a_time_and_a_change_made_twice_is_made_once()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        var step = Plan(task.Id);
        await rig.Work.DispatchAsync(Dispatch(task.Id, step), Token);

        Assert.True(await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Started), Token));
        Assert.False(await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Started), Token));
        Assert.True(await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, Ended(0, outcome: "completed", result: "{\"a\":1}")), Token));
        Assert.False(await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, Ended(9, FailureClass.Unknown)), Token));

        var stored = (await rig.Reads.FindStepRunAsync(step.Id, Token))!;
        Assert.Equal((StepRunStatus.Ended, Noon, Noon.AddMinutes(5), 0), (stored.Status, stored.StartedAt, stored.EndedAt, stored.ExitCode));
        Assert.Equal((StepExitCause.Exited, null, "completed", "{\"a\":1}"), (stored.Cause, stored.Failure, stored.Outcome, stored.Result));
        Assert.Equal(TimeSpan.FromMinutes(5), stored.Duration);
    }

    [Fact]
    public async Task A_step_run_that_ended_before_it_was_seen_started_ends_all_the_same_and_one_that_never_started_cannot_be_checkpointed()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        var step = Plan(task.Id);
        await rig.Work.DispatchAsync(Dispatch(task.Id, step), Token);

        Assert.False(await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Checkpointed, TipAfter: Pushed), Token));
        Assert.True(await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, Ended()), Token));

        var stored = (await rig.Reads.FindStepRunAsync(step.Id, Token))!;
        Assert.Equal((StepRunStatus.Ended, null), (stored.Status, stored.StartedAt));
        Assert.Equal(Noon.AddMinutes(5) - Noon, stored.Duration);
    }

    [Fact]
    public async Task A_checkpoint_is_recorded_with_the_intent_of_its_push_in_one_transaction_and_a_later_conclusion_keeps_what_failure_it_names()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        var step = Plan(task.Id);
        await rig.Work.DispatchAsync(Dispatch(task.Id, step), Token);
        await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, Ended()), Token);
        var push = PushIntent.For(step.Id, new BundleReference(BundleReference.CheckpointPath(task.Id, step.Id), new string('c', 64), Pushed, Commit), Commit).ToNewEffect(task.Id);

        Assert.True(await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Checkpointed, TipAfter: Pushed, Effects: [push]), Token));
        Assert.True(await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Concluded, Failure: FailureClass.AgentError), Token));

        var stored = (await rig.Reads.FindStepRunAsync(step.Id, Token))!;
        Assert.Equal((StepRunStatus.Concluded, Pushed, "agent-error"), (stored.Status, stored.TipAfter, stored.Failure));
        Assert.Single(await rig.Reads.ListUnfinishedEffectsAsync(Token), pending => pending.Effect.Kind == EffectKind.Push);
    }

    [Fact]
    public async Task A_conclusion_that_names_no_failure_leaves_the_failure_the_end_recorded()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        var step = Plan(task.Id);
        await rig.Work.DispatchAsync(Dispatch(task.Id, step), Token);
        await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, Ended(1, FailureClass.ValidationFailure, "failed", null)), Token);

        await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Concluded), Token);

        Assert.Equal("validation-failure", (await rig.Reads.FindStepRunAsync(step.Id, Token))!.Failure);
    }

    [Fact]
    public async Task The_end_of_a_step_moves_the_Task_to_Failed_and_journals_its_label_only_while_the_Task_is_where_the_change_found_it()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        var step = Plan(task.Id);
        await rig.Work.DispatchAsync(Dispatch(task.Id, step), Token);
        var failed = new TaskMove(TaskState.Running, TaskState.Failed, [new NewEffect(task.Id, EffectKeys.StepCause(step.Id), EffectKind.Label, "failed")]);

        await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, Ended(1, FailureClass.Unknown, null, null), Move: failed), Token);

        Assert.Equal(TaskState.Failed, (await rig.Reads.FindTaskAsync(task.Id, Token))!.State);
        Assert.Single(await rig.Reads.ListUnfinishedEffectsAsync(Token), pending => pending.Effect.Detail == "failed");
    }

    [Fact]
    public async Task A_move_of_a_Task_that_is_no_longer_Running_is_not_made_and_its_label_is_not_journaled_though_the_run_ends()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        var step = Plan(task.Id);
        await rig.Work.DispatchAsync(Dispatch(task.Id, step), Token);
        await using (var context = rig.Installation.Database.OpenWriter())
        {
            var id = task.Id.ToString("D");
            await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Tasks" SET "State" = 'cancelled' WHERE "Id" = {id}""", Token);
        }

        var failed = new TaskMove(TaskState.Running, TaskState.Failed, [new NewEffect(task.Id, EffectKeys.StepCause(step.Id), EffectKind.Label, "failed")]);
        Assert.True(await rig.Work.ChangeStepRunAsync(new StepRunChange(step.Id, StepRunStatus.Ended, Ended(1, FailureClass.Unknown, null, null), Move: failed), Token));

        Assert.Equal(TaskState.Cancelled, (await rig.Reads.FindTaskAsync(task.Id, Token))!.State);
        Assert.DoesNotContain(await rig.Reads.ListUnfinishedEffectsAsync(Token), pending => pending.Effect.Detail == "failed");
        Assert.Equal(StepRunStatus.Ended, (await rig.Reads.FindStepRunAsync(step.Id, Token))!.Status);
    }

    [Fact]
    public async Task The_end_of_a_container_is_applied_with_the_Signal_that_reported_it_in_one_transaction()
    {
        using var rig = await StartedAsync();
        var task = await rig.QueueAsync();
        var step = Plan(task.Id);
        await rig.Work.DispatchAsync(Dispatch(task.Id, step), Token);
        var exited = await rig.RequestAsync(Signal.StepExited(step.Id, 0, StepExitCause.Exited, Noon));

        Assert.True(await rig.Work.ApplyAsync(new SignalApplication(exited, SignalStatus.Applied, TaskId: task.Id, StepRun: new StepRunChange(step.Id, StepRunStatus.Ended, Ended())), Token));

        Assert.Equal(StepRunStatus.Ended, (await rig.Reads.FindStepRunAsync(step.Id, Token))!.Status);
        var signal = (await rig.Inbox.FindAsync(exited, Token))!;
        Assert.Equal((SignalStatus.Applied, task.Id), (signal.Status, signal.TaskId));
    }

    [Fact]
    public async Task Tasks_are_listed_by_state_in_the_order_of_their_issue_numbers_and_found_by_id()
    {
        using var rig = await StartedAsync();
        var nine = await rig.QueueAsync(9);
        var three = await rig.QueueAsync(3);
        var other = await rig.QueueAsync(3, repository: 55);

        var queued = await rig.Reads.ListTasksAsync(TaskState.Queued, Token);

        Assert.Equal([other.Id, three.Id, nine.Id], queued.Select(t => t.Id));
        Assert.Empty(await rig.Reads.ListTasksAsync(TaskState.Running, Token));
        Assert.Equal(nine, await rig.Reads.FindTaskAsync(nine.Id, Token));
        Assert.Null(await rig.Reads.FindTaskAsync(Guid.CreateVersion7(), Token));
        Assert.Null(await rig.Reads.FindStepRunAsync(Guid.CreateVersion7(), Token));
        Assert.Null(await rig.Reads.FindTaskSnapshotAsync(nine.Id, Token));
    }

    [Fact]
    public async Task A_Signal_the_loop_appends_for_the_Owner_has_the_id_the_inbox_gave_it_and_a_second_append_of_it_answers_the_same_id()
    {
        using var rig = await StartedAsync();
        var signal = Signal.Start(101, Guid.NewGuid(), Noon);

        var first = await rig.Inbox.AppendOneAsync(signal, Token);
        var again = await rig.Inbox.AppendOneAsync(signal, Token);
        var other = await rig.Inbox.AppendOneAsync(Signal.Stop(101, Guid.NewGuid(), Noon), Token);

        Assert.Equal(first, again);
        Assert.True(other > first);
        var stored = (await rig.Inbox.FindAsync(first, Token))!;
        Assert.Equal((SignalKind.StartRequested, SignalStatus.Pending, Signal.ControlSource, 101L), (stored.Signal.Kind, stored.Status, stored.Signal.Source, stored.Signal.RepositoryId));
        Assert.Null(await rig.Inbox.FindAsync(999, Token));
    }
}
