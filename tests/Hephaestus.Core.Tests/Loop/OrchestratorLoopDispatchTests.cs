using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Readiness;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// Story 3.14, AD-1, AD-16, FR-2, FR-14: the loop dispatches the one eligible Task, moves it to Running only from Queued, never has two Running, reads the Project Configuration from the Base Branch once, validates it, and snapshots it onto the Task with
/// its commit id and the resolved image digest. A Task that cannot start is passed over and the Tasks of other Projects are not held back.
/// </summary>
public class OrchestratorLoopDispatchTests
{
    private const long OtherRepository = 202;

    private static CancellationToken Token => RunRig.Token;

    [Fact]
    public async Task A_Queued_Task_of_a_Project_with_an_open_window_moves_to_Running_with_its_snapshot_its_first_step_and_the_intent_of_its_label()
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        await rig.StartAsync();

        var report = await rig.TickAsync();

        var task = rig.Task();
        Assert.Equal((TaskState.Running, 1), (task.State, report.TasksStarted));
        var snapshot = await rig.Stores.FindTaskSnapshotAsync(task.Id, Token);
        Assert.Equal((RunRig.StartCommit, FakeRuntime.Image.Digest, "ubuntu:24.04"), (snapshot!.CommitId, snapshot.Image.Digest, snapshot.Configuration.Image));
        Assert.Equal((CLibrary.Glibc, "opus", Effort.High.Tag()), (snapshot.Image.Libc, snapshot.Configuration.PrimaryAlias, Effort.High.Tag()));
        Assert.Equal(["dotnet build", "dotnet test"], snapshot.Configuration.Validation);
        var step = Assert.Single(rig.Steps());
        Assert.Equal((StepKind.Implementation, 0, 1, RunRig.StartCommit), (step.Kind, step.Round, step.Sequence, step.TipBefore));
        Assert.Equal((Agent.ClaudeCode, "claude-opus-5-5", Effort.High, "default"), (step.Agent!.Value, step.Model, step.Effort!.Value, step.Methodology));
        Assert.StartsWith("implementation.md@sha256:", step.Template, StringComparison.Ordinal);
        Assert.Equal(rig.Time.Now + StepDeadlines.DefaultAgentLimit, step.Deadline);
        var label = Assert.Single(rig.Stores.Journal, e => e.Entry == JournalEntryKind.Intent && e.Effect.Kind == EffectKind.Label && e.Effect.Detail == "running");
        Assert.Equal(task.Id, label.Effect.TaskId);
    }

    [Fact]
    public async Task The_deadline_of_the_first_step_is_the_Project_limit_held_to_the_installation_limit()
    {
        var rig = new RunRig();
        rig.Stores.Limits = new InstallationLimits(TimeSpan.FromMinutes(30), null, null);
        rig.SetConfiguration(RunRig.RepositoryId, RunRig.ConfigurationFile + "\nlimits:\n  taskDuration: 20m\n", RunRig.StartCommit);
        await rig.QueueAsync();
        await rig.StartAsync();

        await rig.TickAsync();

        Assert.Equal(rig.Time.Now + TimeSpan.FromMinutes(20), rig.LastStep().Deadline);
    }

    [Fact]
    public async Task The_Task_with_the_lowest_issue_number_starts_first_and_no_second_Task_runs_while_it_does()
    {
        var rig = new RunRig();
        await rig.QueueAsync(eventId: "issue-event:1", issue: 9, title: "Later");
        await rig.QueueAsync(eventId: "issue-event:2", issue: 3, title: "Sooner");
        await rig.StartAsync();

        await rig.TickAsync(3);

        Assert.Equal((TaskState.Running, TaskState.Queued), (rig.Task(3).State, rig.Task(9).State));
        Assert.Single(rig.Stores.Tasks, task => task.State == TaskState.Running);
        Assert.Empty(rig.Steps(9));
    }

    [Fact]
    public async Task A_Task_is_started_only_from_Queued_and_the_store_refuses_a_second_start_of_a_Task_that_runs()
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();
        var running = rig.Task();
        var step = rig.LastStep();

        var again = await rig.Stores.DispatchAsync(new TaskDispatch(running.Id, null, new NewStepRun(Guid.CreateVersion7(), running.Id, StepKind.Implementation, 0, rig.Time.Now, rig.Time.Now, RunRig.StartCommit), []), Token);

        Assert.False(again);
        Assert.Single(rig.Steps());
        Assert.Equal(step, rig.LastStep());
    }

    [Fact]
    public async Task No_Task_starts_without_an_open_window_for_its_Project_and_nothing_is_read_from_GitHub()
    {
        var rig = new RunRig();
        await rig.QueueAsync();

        await rig.TickAsync(3);

        Assert.Equal(TaskState.Queued, rig.Task().State);
        Assert.Equal(0, rig.Stores.Github.FileReads);
        Assert.Empty(rig.Stores.StepRuns);
    }

    [Fact]
    public async Task The_file_is_read_from_the_Base_Branch_once_and_a_Task_that_starts_again_keeps_the_snapshot_it_has()
    {
        var rig = new RunRig();
        var queued = await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();
        var snapshot = await rig.Stores.FindTaskSnapshotAsync(queued.Id, Token);

        // The Task waited for the Owner and was answered: it is Queued again, the file has changed, and a window is open.
        rig.Stores.Tasks[0] = rig.Stores.Tasks[0] with { State = TaskState.Queued };
        rig.SetConfiguration(RunRig.RepositoryId, "primaryAlias: opus\nvalidation:\n  - echo changed\nimage: alpine:3\n", new string('f', 40));
        rig.Stores.Windows.Clear();
        await rig.StartAsync();
        await rig.TickAsync();

        Assert.Equal(TaskState.Running, rig.Task().State);
        Assert.Equal(1, rig.Stores.Github.FileReads);
        Assert.Equal(snapshot, await rig.Stores.FindTaskSnapshotAsync(queued.Id, Token));
        Assert.Equal(2, rig.Steps().Count);
    }

    [Theory]
    [InlineData("primaryAlias: opus\nimage: ubuntu:24.04\n", "validation")]
    [InlineData("primaryAlias: nobody\nvalidation:\n  - x\nimage: ubuntu:24.04\n", "invalid")]
    [InlineData("this: is: not: yaml: [", "invalid")]
    public async Task A_Task_whose_configuration_is_invalid_is_not_started_and_the_log_line_says_so(string file, string _)
    {
        var rig = new RunRig();
        rig.SetConfiguration(RunRig.RepositoryId, file, RunRig.StartCommit);
        await rig.QueueAsync();
        await rig.StartAsync();

        var report = await rig.TickAsync();

        Assert.Equal((TaskState.Queued, 0), (rig.Task().State, report.TasksStarted));
        Assert.Contains(report.TaskProblems!, line => line.Contains("configuration is invalid", StringComparison.Ordinal) && line.Contains(rig.Task().Id.ToString(), StringComparison.Ordinal));
        Assert.Empty(rig.Stores.StepRuns);
        Assert.Empty(rig.Runtime.Images);
    }

    [Fact]
    public async Task A_Task_whose_Project_has_no_configuration_file_is_not_started()
    {
        var rig = new RunRig(configuration: null);
        await rig.QueueAsync();
        await rig.StartAsync();

        var report = await rig.TickAsync();

        Assert.Equal(TaskState.Queued, rig.Task().State);
        Assert.Contains(report.TaskProblems!, line => line.Contains("configuration is missing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_GitHub_that_cannot_be_asked_leaves_the_Task_Queued_and_the_tick_reports_the_failure()
    {
        var rig = new RunRig();
        rig.Stores.Github.ConfigurationFiles[RunRig.RepositoryId] = GitHubFileResult.Failed(GitHubFileFailure.Unreachable);
        await rig.QueueAsync();
        await rig.StartAsync();

        var report = await rig.TickAsync();

        Assert.Equal(TaskState.Queued, rig.Task().State);
        Assert.Contains(report.TaskProblems!, line => line.Contains("could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Task_that_could_not_start_is_tried_again_after_a_minute_and_not_before_so_GitHub_is_not_asked_on_every_tick()
    {
        var rig = new RunRig();
        rig.SetConfiguration(RunRig.RepositoryId, "primaryAlias: opus\nimage: ubuntu:24.04\n", RunRig.StartCommit);
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();
        await rig.TickAsync(3);
        Assert.Equal(1, rig.Stores.Github.FileReads);

        rig.SetConfiguration(RunRig.RepositoryId, RunRig.ConfigurationFile, RunRig.StartCommit);
        rig.Time.Advance(Hephaestus.Core.Loop.OrchestratorLoop.StartRetryDelay + TimeSpan.FromSeconds(1));
        await rig.TickAsync();

        Assert.Equal(2, rig.Stores.Github.FileReads);
        Assert.Equal(TaskState.Running, rig.Task().State);
    }

    [Fact]
    public async Task A_Task_that_cannot_start_does_not_hold_back_a_Task_of_another_Project()
    {
        var rig = new RunRig();
        rig.Stores.Projects.Add(new Project(OtherRepository, "acme/api", "main", DateTimeOffset.UnixEpoch, null));
        rig.SetConfiguration(RunRig.RepositoryId, "primaryAlias: opus\nimage: ubuntu:24.04\n", RunRig.StartCommit);
        rig.SetConfiguration(OtherRepository, RunRig.ConfigurationFile, new string('9', 40));
        await rig.QueueAsync(issue: 2, title: "Broken Project");
        await rig.Stores.AppendAsync([LoopStores.Labeled("issue-event:other", issue: 8, repository: OtherRepository, title: "Works")], Token);
        await rig.TickAsync();
        await rig.StartAsync();
        await rig.StartAsync(OtherRepository);

        await rig.TickAsync();

        var other = rig.Stores.Tasks.Single(task => task.RepositoryId == OtherRepository);
        Assert.Equal((TaskState.Queued, TaskState.Running), (rig.Task(2).State, other.State));
        Assert.Equal(new string('9', 40), (await rig.Stores.FindTaskSnapshotAsync(other.Id, Token))!.CommitId);
    }

    [Fact]
    public async Task An_image_that_cannot_be_used_leaves_the_Task_Queued()
    {
        var rig = new RunRig();
        rig.Runtime.Throws[nameof(FakeRuntime.ResolveImageAsync)] = new ImageNotUsableException(new Hephaestus.Core.Localization.Message("workspace.probe.docker"));
        await rig.QueueAsync();
        await rig.StartAsync();

        var report = await rig.TickAsync();

        Assert.Equal(TaskState.Queued, rig.Task().State);
        Assert.Contains(report.TaskProblems!, line => line.Contains("image cannot be used", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Task_whose_Agent_is_not_ready_to_be_driven_is_not_started_and_the_log_line_names_what_is_wrong()
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        await rig.StartAsync();
        rig.Stores.AgentSettings.Clear();

        var withoutSignIn = await rig.TickAsync();

        Assert.Equal(TaskState.Queued, rig.Task().State);
        Assert.Contains(withoutSignIn.TaskProblems!, line => line.Contains("nobody has chosen how claude-code signs in", StringComparison.Ordinal));

        rig.Stores.AgentSettings.Add(new AgentSetting(Agent.ClaudeCode, AuthenticationMode.ApiKey, rig.Time.Now));
        rig.Adapter.SupportsEverything = false;
        rig.Time.Advance(TimeSpan.FromMinutes(2));
        var unsupported = await rig.TickAsync();

        Assert.Contains(unsupported.TaskProblems!, line => line.Contains("does not support the effort high", StringComparison.Ordinal));

        rig.Adapter.SupportsEverything = true;
        rig.Stores.AgentCapacities.Add(new AgentCapacity(Agent.ClaudeCode, AuthenticationMode.ApiKey, AgentReadiness.Unavailable, null, [], rig.Time.Now, null));
        rig.Time.Advance(TimeSpan.FromMinutes(2));
        var exhausted = await rig.TickAsync();

        Assert.Contains(exhausted.TaskProblems!, line => line.Contains("has no capacity", StringComparison.Ordinal));
        Assert.Equal(TaskState.Queued, rig.Task().State);
    }

    [Fact]
    public async Task A_Project_that_names_no_primary_Alias_or_one_that_names_no_Agent_does_not_start_its_Task()
    {
        var rig = new RunRig();
        rig.SetConfiguration(RunRig.RepositoryId, "validation:\n  - x\nimage: ubuntu:24.04\n", RunRig.StartCommit);
        await rig.QueueAsync();
        await rig.StartAsync();

        var report = await rig.TickAsync();

        Assert.Equal(TaskState.Queued, rig.Task().State);
        Assert.Contains(report.TaskProblems!, line => line.Contains("names no primary Alias", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_loop_with_no_runtime_starts_no_Task_but_still_closes_a_window_that_has_nothing_to_work()
    {
        var rig = new RunRig();
        var bare = new Hephaestus.Core.Loop.OrchestratorLoop(rig.Stores, rig.Stores, rig.Stores, rig.Stores.Github, rig.Stores.Notifier, rig.Time, new Hephaestus.Core.Loop.LoopHeartbeat(rig.Time));
        await rig.StartAsync();

        await bare.TickAsync(Token);

        Assert.Equal(Hephaestus.Core.Windows.WindowEnd.Drained, Assert.Single(rig.Stores.Windows).End);
    }
}
