using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.3, AD-18, FR-19, FR-4, FR-53: the runtime keeps what a step printed, reports its end exactly once with the runtime's own cause, stops it at its deadline
/// and nothing else does, and removes what it made for the step run. Against an Engine in memory and a clock the test moves.
/// </summary>
public class StepWatcherTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DockerApiException Status(HttpStatusCode code) => new(code, "refused");

    /// <summary>The container runs for a number of looks, then is as the state says.</summary>
    private static Func<string, int, Task<ContainerInspectResponse>> RunsThenEnds(int looks, ContainerInspectResponse ended) =>
        (_, call) => Task.FromResult(call <= looks ? DockerDouble.Inspected("running") : ended);

    [Fact]
    public async Task A_step_that_ends_by_itself_leaves_one_output_file_with_both_streams_in_the_order_they_came_one_Signal_and_nothing_of_its_own_behind()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(2, DockerDouble.Inspected("exited", exitCode: 0));
        rig.Docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((1, "building\n"), (2, "warning: slow\n"), (1, "done\n")));

        await rig.WatchToTheEndAsync();

        var file = Assert.Single(rig.Output.Files).Value;
        Assert.Equal("building\nwarning: slow\ndone\n", file.Text);
        Assert.True(file.Complete);
        Assert.Equal((WatchRig.TaskId, WatchRig.StepRunId, WatchRig.Container), (file.Run.TaskId, file.Run.StepRunId, file.Run.ContainerId));
        var signal = Assert.Single(rig.Inbox.Appended);
        Assert.Equal(SignalKind.StepExited, signal.Kind);
        Assert.Equal((Signal.RuntimeSource, WatchRig.StepRunId.ToString("D"), WatchRig.StepRunId, 0, StepExitCause.Exited), (signal.Source, signal.SourceEventId, signal.StepRunId, signal.ExitCode, signal.Cause));
        Assert.Equal(WatchRig.Noon + rig.Delays.Aggregate(TimeSpan.Zero, (sum, delay) => sum + delay), signal.OccurredAt);
        Assert.Empty(rig.Problems);
    }

    [Fact]
    public async Task The_end_is_output_complete_then_the_Signal_then_the_container_the_proxy_the_network_and_the_volumes_of_the_step_run_removed()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));

        await rig.WatchToTheEndAsync();

        // The output is recorded beside the look at the container, so only the order of the end is fixed. The proxy's log is read before anything is removed.
        var end = rig.Docker.Calls.Where(call => call is "complete output" or "append signal" || call.StartsWith("remove", StringComparison.Ordinal) || call.StartsWith("logs hephaestus-egress", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [
                "complete output",
                $"logs hephaestus-egress-{WatchRig.StepRunId}",
                "append signal",
                $"remove {WatchRig.Container}",
                $"remove hephaestus-egress-{WatchRig.StepRunId}",
                $"remove network hephaestus-net-{WatchRig.StepRunId}",
                $"remove volume hephaestus-context-{WatchRig.StepRunId}",
                $"remove volume hephaestus-methodology-{WatchRig.StepRunId}",
            ],
            end);
        Assert.All(rig.Docker.Removals, removal => Assert.True(removal.Force));
        Assert.DoesNotContain(rig.Docker.RemovedVolumes, volume => volume.StartsWith("hephaestus-ws-", StringComparison.Ordinal));
        Assert.True(Assert.Single(rig.Output.Files).Value.Disposed);
    }

    [Fact]
    public async Task The_output_is_followed_from_the_start_of_the_containers_log_on_both_streams()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));

        await rig.WatchToTheEndAsync();

        var request = Assert.Single(rig.Docker.LogRequests);
        Assert.Equal((true, true, true), (request.ShowStdout, request.ShowStderr, request.Follow));
        Assert.Null(request.Since);
        Assert.Null(request.Tail);
    }

    [Fact]
    public async Task A_secret_that_is_split_across_two_chunks_of_output_is_masked_whole_and_no_part_of_it_is_stored()
    {
        const string Secret = "sk-test-0123456789-abcdefghij";
        await using var rig = new WatchRig([Secret]);
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((1, "key=sk-test-0123"), (2, "456789-abcdefghij and more\n")));

        await rig.WatchToTheEndAsync();

        var text = Assert.Single(rig.Output.Files).Value.Text;
        Assert.Equal("key=*** and more\n", text);
        Assert.DoesNotContain("sk-test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghij", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secret_at_the_very_end_of_the_output_is_masked_when_the_output_ends()
    {
        const string Secret = "sk-test-0123456789-abcdefghij";
        await using var rig = new WatchRig([Secret]);
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((1, "last words: sk-test-0123456789-abcdef"), (1, "ghij")));

        await rig.WatchToTheEndAsync();

        Assert.Equal("last words: ***", Assert.Single(rig.Output.Files).Value.Text);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    public async Task The_exit_code_is_the_one_the_container_ended_with_whatever_it_is(long code)
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(1, DockerDouble.Inspected("exited", exitCode: code));

        await rig.WatchToTheEndAsync();

        var signal = Assert.Single(rig.Inbox.Appended);
        Assert.Equal(((int)code, StepExitCause.Exited), (signal.ExitCode, signal.Cause));
    }

    [Fact]
    public async Task A_step_still_running_when_its_deadline_passes_is_stopped_by_the_runtime_once_and_ends_with_the_cause_timeout_and_the_output_so_far()
    {
        await using var rig = new WatchRig();
        var stopped = false;
        rig.Docker.StopFails = _ =>
        {
            stopped = true;
            return null;
        };
        rig.Docker.Inspector = (_, _) => Task.FromResult(stopped ? DockerDouble.Inspected("exited", exitCode: 137) : DockerDouble.Inspected("running"));
        rig.Docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((1, "step 1 of 9\n"), (1, "step 2 of 9\n")));

        await rig.WatchToTheEndAsync(rig.Step(TimeSpan.FromMinutes(30)));

        var stop = Assert.Single(rig.Docker.Stops);
        Assert.Equal(rig.Options.StopGraceSeconds, stop.WaitBeforeKillSeconds);
        var signal = Assert.Single(rig.Inbox.Appended);
        Assert.Equal((137, StepExitCause.Timeout), (signal.ExitCode, signal.Cause));
        var file = Assert.Single(rig.Output.Files).Value;
        Assert.Equal("step 1 of 9\nstep 2 of 9\n", file.Text);
        Assert.True(file.Complete);
        // It was stopped when the deadline had passed and not before.
        Assert.True(rig.Time.Now >= WatchRig.Noon.AddMinutes(30));
        Assert.True(rig.Time.Now < WatchRig.Noon.AddMinutes(31));
        Assert.Contains($"remove {WatchRig.Container}", rig.Docker.Calls);
    }

    [Fact]
    public async Task A_step_that_ends_before_its_deadline_is_never_stopped()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(5, DockerDouble.Inspected("exited"));

        await rig.WatchToTheEndAsync(rig.Step(TimeSpan.FromHours(2)));

        Assert.Empty(rig.Docker.Stops);
        Assert.Equal(StepExitCause.Exited, Assert.Single(rig.Inbox.Appended).Cause);
    }

    [Fact]
    public async Task The_container_is_looked_at_once_a_second_and_a_deadline_between_two_looks_is_acted_on_when_it_passes()
    {
        await using var rig = new WatchRig();
        var stopped = false;
        rig.Docker.StopFails = _ =>
        {
            stopped = true;
            return null;
        };
        rig.Docker.Inspector = (_, _) => Task.FromResult(stopped ? DockerDouble.Inspected("exited", exitCode: 137) : DockerDouble.Inspected("running"));

        await rig.WatchToTheEndAsync(rig.Step(TimeSpan.FromSeconds(2.5)));

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.5)], rig.Delays);
        Assert.Equal(WatchRig.Noon.AddSeconds(2.5), rig.Time.Now);
    }

    [Fact]
    public async Task A_deadline_that_has_passed_before_the_step_was_watched_stops_it_at_the_first_look()
    {
        await using var rig = new WatchRig();
        var stopped = false;
        rig.Docker.StopFails = _ =>
        {
            stopped = true;
            return null;
        };
        rig.Docker.Inspector = (_, _) => Task.FromResult(stopped ? DockerDouble.Inspected("exited", exitCode: 143) : DockerDouble.Inspected("running"));

        await rig.WatchToTheEndAsync(rig.Step(TimeSpan.FromMinutes(-5)));

        Assert.Single(rig.Docker.Stops);
        Assert.Equal((143, StepExitCause.Timeout), (Assert.Single(rig.Inbox.Appended).ExitCode, Assert.Single(rig.Inbox.Appended).Cause));
    }

    [Fact]
    public async Task A_stop_that_finds_the_container_already_stopped_by_itself_is_not_an_error_and_not_a_timeout()
    {
        await using var rig = new WatchRig();
        rig.Docker.StopFails = _ => Status(HttpStatusCode.NotModified);
        rig.Docker.Inspector = (_, call) => Task.FromResult(call < 3 ? DockerDouble.Inspected("running") : DockerDouble.Inspected("exited", exitCode: 5));

        await rig.WatchToTheEndAsync(rig.Step(TimeSpan.FromSeconds(1)));

        Assert.Single(rig.Docker.Stops);
        Assert.Empty(rig.Problems);
        var signal = Assert.Single(rig.Inbox.Appended);
        Assert.Equal((5, StepExitCause.Exited), (signal.ExitCode, signal.Cause));
    }

    [Fact]
    public async Task A_stop_that_finds_the_container_gone_is_not_an_error_and_the_step_ends_as_lost()
    {
        await using var rig = new WatchRig();
        rig.Docker.StopFails = _ => Status(HttpStatusCode.NotFound);
        rig.Docker.Inspector = (_, call) => call < 3 ? Task.FromResult(DockerDouble.Inspected("running")) : Task.FromException<ContainerInspectResponse>(DockerDouble.NotFound());

        await rig.WatchToTheEndAsync(rig.Step(TimeSpan.FromSeconds(1)));

        Assert.Empty(rig.Problems);
        Assert.Equal((null, StepExitCause.Lost), (Assert.Single(rig.Inbox.Appended).ExitCode, Assert.Single(rig.Inbox.Appended).Cause));
    }

    [Fact]
    public async Task A_container_the_kernel_ended_for_its_memory_ends_with_the_cause_out_of_memory()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(1, DockerDouble.Inspected("exited", exitCode: 137, oomKilled: true));

        await rig.WatchToTheEndAsync();

        var signal = Assert.Single(rig.Inbox.Appended);
        Assert.Equal((137, StepExitCause.OutOfMemory), (signal.ExitCode, signal.Cause));
        Assert.Empty(rig.Docker.Stops);
    }

    [Fact]
    public async Task A_container_that_is_dead_has_ended()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("dead", exitCode: 1));

        await rig.WatchToTheEndAsync();

        Assert.Equal((1, StepExitCause.Exited), (Assert.Single(rig.Inbox.Appended).ExitCode, Assert.Single(rig.Inbox.Appended).Cause));
    }

    [Fact]
    public async Task A_container_that_is_gone_when_it_is_looked_at_ends_with_the_cause_lost_and_no_exit_code()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = (_, call) => call < 3 ? Task.FromResult(DockerDouble.Inspected("running")) : Task.FromException<ContainerInspectResponse>(DockerDouble.NotFound());
        rig.Docker.Logs = (_, _) => Task.FromException<MultiplexedStream>(DockerDouble.NotFound());

        await rig.WatchToTheEndAsync();

        var signal = Assert.Single(rig.Inbox.Appended);
        Assert.Equal((null, StepExitCause.Lost), (signal.ExitCode, signal.Cause));
        Assert.True(Assert.Single(rig.Output.Files).Value.Complete);
    }

    [Fact]
    public async Task A_container_with_no_state_in_the_answer_is_one_that_has_not_ended()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = (_, call) => Task.FromResult(call < 3 ? new ContainerInspectResponse() : DockerDouble.Inspected("exited"));

        await rig.WatchToTheEndAsync();

        Assert.Equal(StepExitCause.Exited, Assert.Single(rig.Inbox.Appended).Cause);
    }

    [Fact]
    public async Task A_step_run_that_is_watched_twice_is_seen_to_its_end_once()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(2, DockerDouble.Inspected("exited"));

        rig.Watcher.Watch(rig.Step());
        rig.Watcher.Watch(rig.Step());
        await rig.Watcher.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), Token);

        Assert.Single(rig.Inbox.Appended);
        Assert.Single(rig.Docker.LogRequests);
        Assert.Single(rig.Output.Files);
    }

    [Fact]
    public async Task A_step_run_that_has_been_seen_to_its_end_can_be_watched_again_and_its_Signal_is_not_appended_a_second_time()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((1, "output\n")));

        await rig.WatchToTheEndAsync();
        await rig.WatchToTheEndAsync();

        Assert.Single(rig.Inbox.Appended);
        Assert.Equal(2, rig.Docker.LogRequests.Count);
        Assert.Equal("output\n", Assert.Single(rig.Output.Files).Value.Text);
    }

    [Fact]
    public async Task Two_step_runs_are_watched_independently()
    {
        await using var rig = new WatchRig();
        var other = Guid.Parse("018f4b6e-0000-7000-8000-000000000009");
        rig.Docker.Inspector = (id, _) => Task.FromResult(DockerDouble.Inspected("exited", exitCode: id == "container-2" ? 3 : 0));

        rig.Watcher.Watch(rig.Step());
        rig.Watcher.Watch(new WatchedStep(WatchRig.TaskId, other, "container-2", WatchRig.Noon.AddHours(1)));
        await rig.Watcher.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), Token);

        Assert.Equal([0, 3], rig.Inbox.Appended.OrderBy(signal => signal.ExitCode).Select(signal => signal.ExitCode));
        Assert.Equal(2, rig.Output.Files.Count);
    }

    [Fact]
    public async Task What_fails_while_a_step_is_watched_is_reported_and_tried_again_after_the_retry_interval_and_the_step_still_ends_once()
    {
        await using var rig = new WatchRig();
        rig.Output.BeginFails = attempt => attempt == 1 ? new IOException("disk") : null;
        rig.Docker.Inspector = (_, call) => call switch
        {
            1 => Task.FromException<ContainerInspectResponse>(new HttpRequestException("engine restarting")),
            2 => Task.FromResult(DockerDouble.Inspected("running")),
            _ => Task.FromResult(DockerDouble.Inspected("exited", exitCode: 0)),
        };
        rig.Inbox.Fails = attempt => attempt <= 2 ? new InvalidOperationException("database is locked") : null;

        await rig.WatchToTheEndAsync();

        Assert.Single(rig.Inbox.Appended);
        Assert.Equal(
            [
                "The output file of step run 018f4b6e-0000-7000-8000-000000000002 could not be started. (IOException)",
                "Step run 018f4b6e-0000-7000-8000-000000000002 could not be looked at. (HttpRequestException)",
                "The end of step run 018f4b6e-0000-7000-8000-000000000002 could not be reported. (InvalidOperationException)",
                "The end of step run 018f4b6e-0000-7000-8000-000000000002 could not be reported. (InvalidOperationException)",
            ],
            rig.Problems);
        Assert.Equal(4, rig.Delays.Count(delay => delay == rig.Options.RetryInterval));
        Assert.Contains($"remove {WatchRig.Container}", rig.Docker.Calls);
    }

    [Fact]
    public async Task A_stop_that_fails_is_tried_again_and_the_deadline_still_ends_the_step_as_a_timeout()
    {
        await using var rig = new WatchRig();
        var attempts = 0;
        rig.Docker.StopFails = _ => ++attempts == 1 ? new HttpRequestException("engine busy") : null;
        rig.Docker.Inspector = (_, _) => Task.FromResult(attempts >= 2 ? DockerDouble.Inspected("exited", exitCode: 137) : DockerDouble.Inspected("running"));

        await rig.WatchToTheEndAsync(rig.Step(TimeSpan.FromSeconds(1)));

        Assert.Equal(2, rig.Docker.Stops.Count);
        Assert.Equal(StepExitCause.Timeout, Assert.Single(rig.Inbox.Appended).Cause);
        Assert.Contains(rig.Problems, problem => problem.Contains("could not be stopped at its deadline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Output_that_cannot_be_read_is_reported_and_does_not_keep_the_step_from_ending()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(1, DockerDouble.Inspected("exited"));
        rig.Docker.Logs = (_, _) => Task.FromException<MultiplexedStream>(new HttpRequestException("no log driver"));

        await rig.WatchToTheEndAsync();

        Assert.Equal(["The output of step run 018f4b6e-0000-7000-8000-000000000002 could not be read to its end. (HttpRequestException)"], rig.Problems);
        Assert.Equal(StepExitCause.Exited, Assert.Single(rig.Inbox.Appended).Cause);
        Assert.True(Assert.Single(rig.Output.Files).Value.Complete);
    }

    [Fact]
    public async Task Output_that_does_not_end_after_its_container_did_is_kept_as_far_as_it_came_once_the_drain_limit_has_passed()
    {
        var options = new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, Hephaestus.Core.AgentBinaries.AgentArchitecture.X64) { OutputDrainLimit = TimeSpan.FromMilliseconds(100) };
        await using var rig = new WatchRig(options: options);
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.Logs = (_, token) => Task.FromResult(new MultiplexedStream(new EndlessLog(DockerDouble.Frames((1, "came before the end\n")), token), multiplexed: true));

        await rig.WatchToTheEndAsync();

        Assert.Equal("came before the end\n", Assert.Single(rig.Output.Files).Value.Text);
        Assert.Single(rig.Problems);
        Assert.Contains("did not end after its container did", rig.Problems[0], StringComparison.Ordinal);
        Assert.Single(rig.Inbox.Appended);
    }

    [Fact]
    public async Task A_container_or_volume_that_is_already_gone_is_what_removal_is_for()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.RemoveFails = _ => DockerDouble.NotFound();
        rig.Docker.VolumeRemoveFails = _ => DockerDouble.NotFound();

        await rig.WatchToTheEndAsync();

        Assert.Empty(rig.Problems);
        Assert.Equal(2, rig.Docker.RemovedVolumes.Count);
        Assert.Single(rig.Inbox.Appended);
    }

    [Fact]
    public async Task A_removal_that_fails_is_reported_and_left_for_cleanup_and_the_Signal_stands()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.VolumeRemoveFails = volume => volume.StartsWith("hephaestus-context-", StringComparison.Ordinal) ? Status(HttpStatusCode.Conflict) : null;

        await rig.WatchToTheEndAsync();

        Assert.Equal(["A container, network, or volume of step run 018f4b6e-0000-7000-8000-000000000002 could not be removed; it is left for cleanup. (DockerApiException)"], rig.Problems);
        Assert.Single(rig.Inbox.Appended);
    }

    [Fact]
    public async Task Output_that_cannot_be_read_for_a_reason_the_runtime_does_not_know_is_reported_too_and_does_not_keep_the_step_from_ending()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.Logs = (_, _) => throw new InvalidProgramException("a bug");

        await rig.WatchToTheEndAsync();

        Assert.Equal(["The output of step run 018f4b6e-0000-7000-8000-000000000002 could not be read to its end. (InvalidProgramException)"], rig.Problems);
        Assert.Equal(StepExitCause.Exited, Assert.Single(rig.Inbox.Appended).Cause);
        Assert.True(Assert.Single(rig.Output.Files).Value.Complete);
    }

    [Fact]
    public async Task A_container_that_cannot_be_removed_does_not_keep_the_volumes_from_being_tried()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.RemoveFails = container => container == WatchRig.Container ? Status(HttpStatusCode.Conflict) : null;

        await rig.WatchToTheEndAsync();

        Assert.Equal(["A container, network, or volume of step run 018f4b6e-0000-7000-8000-000000000002 could not be removed; it is left for cleanup. (DockerApiException)"], rig.Problems);
        Assert.Equal(2, rig.Docker.RemovedVolumes.Count);
        Assert.Contains($"hephaestus-egress-{WatchRig.StepRunId}", rig.Docker.Removed);
    }

    [Fact]
    public async Task A_removal_that_fails_for_a_reason_the_runtime_does_not_know_ends_the_watch_after_the_Signal_and_is_reported()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.RemoveFails = _ => new InvalidProgramException("a bug");

        await rig.WatchToTheEndAsync();

        Assert.Equal(["Step run 018f4b6e-0000-7000-8000-000000000002 is no longer watched. (InvalidProgramException)"], rig.Problems);
        Assert.Single(rig.Inbox.Appended);
    }

    [Fact]
    public async Task A_store_that_keeps_failing_does_not_hold_back_the_deadline_and_the_step_still_ends_without_its_output()
    {
        var options = new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, Hephaestus.Core.AgentBinaries.AgentArchitecture.X64) { OutputDrainLimit = TimeSpan.FromMilliseconds(100) };
        await using var rig = new WatchRig(options: options);
        rig.Output.BeginFails = _ => new IOException("disk is full");
        var stopped = false;
        rig.Docker.StopFails = _ =>
        {
            stopped = true;
            return null;
        };
        rig.Docker.Inspector = (_, _) => Task.FromResult(stopped ? DockerDouble.Inspected("exited", exitCode: 137) : DockerDouble.Inspected("running"));

        await rig.WatchToTheEndAsync(rig.Step(TimeSpan.FromSeconds(3)));

        Assert.Single(rig.Docker.Stops);
        Assert.Equal(StepExitCause.Timeout, Assert.Single(rig.Inbox.Appended).Cause);
        Assert.Empty(rig.Output.Files);
        Assert.Contains("The output file of step run 018f4b6e-0000-7000-8000-000000000002 was never started, so its output is lost.", rig.Problems);
    }

    [Fact]
    public async Task A_file_that_cannot_be_completed_is_completed_again_and_the_step_ends_once_with_all_its_output()
    {
        await using var rig = new WatchRig();
        rig.Output.CompleteFails = attempt => attempt == 1 ? new IOException("database is locked") : null;
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((1, "all of it\n")));

        await rig.WatchToTheEndAsync();

        var file = Assert.Single(rig.Output.Files).Value;
        Assert.Equal("all of it\n", file.Text);
        Assert.True(file.Complete);
        Assert.Single(rig.Inbox.Appended);
        Assert.Equal(["The output file of step run 018f4b6e-0000-7000-8000-000000000002 could not be completed. (IOException)"], rig.Problems);
    }

    [Fact]
    public async Task A_log_read_that_does_not_honor_cancellation_is_ended_by_closing_the_stream()
    {
        var options = new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, Hephaestus.Core.AgentBinaries.AgentArchitecture.X64) { OutputDrainLimit = TimeSpan.FromMilliseconds(100) };
        await using var rig = new WatchRig(options: options);
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        var log = new DeafLog(DockerDouble.Frames((1, "before it hung\n")));
        rig.Docker.Logs = (_, _) => Task.FromResult(new MultiplexedStream(log, multiplexed: true));

        await rig.WatchToTheEndAsync();

        Assert.Equal("before it hung\n", Assert.Single(rig.Output.Files).Value.Text);
        Assert.True(log.Closed);
        Assert.Single(rig.Inbox.Appended);
    }

    [Fact]
    public async Task A_listener_that_fails_does_not_end_the_watch()
    {
        var docker = new DockerDouble { Inspector = (_, call) => call == 1 ? Task.FromException<ContainerInspectResponse>(new HttpRequestException("once")) : Task.FromResult(DockerDouble.Inspected("exited")) };
        var inbox = new InMemoryInbox(docker.Calls);
        await using var watcher = new StepWatcher(
            docker.Client,
            new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, Hephaestus.Core.AgentBinaries.AgentArchitecture.X64) { RetryInterval = TimeSpan.FromMilliseconds(1) },
            inbox,
            new InMemoryOutput(docker.Calls),
            new Hephaestus.Core.Secrets.SecretMasker(NSubstitute.Substitute.For<Hephaestus.Core.Ports.ISecretStore>()),
            TimeProvider.System,
            (_, _) => throw new InvalidOperationException("the log is gone"));

        watcher.Watch(new WatchedStep(WatchRig.TaskId, WatchRig.StepRunId, WatchRig.Container, DateTimeOffset.UtcNow.AddHours(1)));
        await watcher.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), Token);

        Assert.Single(inbox.Appended);
    }

    [Fact]
    public async Task Stopping_the_watcher_stops_every_watch_and_leaves_the_container_and_reports_nothing()
    {
        var rig = new WatchRig();
        var looked = new TaskCompletionSource();
        rig.Docker.Inspector = (_, _) =>
        {
            looked.TrySetResult();
            return Task.FromResult(DockerDouble.Inspected("running"));
        };

        rig.Watcher.Watch(rig.Step());
        await looked.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        await rig.Watcher.DisposeAsync();
        await rig.Watcher.DisposeAsync();

        Assert.Empty(rig.Inbox.Appended);
        Assert.Empty(rig.Docker.Removed);
        Assert.Empty(rig.Docker.Stops);
        Assert.Empty(rig.Problems);
        rig.Watcher.Watch(rig.Step());
        await rig.Watcher.WhenIdleAsync();
        Assert.Single(rig.Docker.LogRequests);
    }

    [Fact]
    public async Task A_watcher_with_nobody_to_tell_of_problems_still_sees_a_step_to_its_end()
    {
        var docker = new DockerDouble { Inspector = (_, call) => call == 1 ? Task.FromException<ContainerInspectResponse>(new HttpRequestException("once")) : Task.FromResult(DockerDouble.Inspected("exited")) };
        var calls = docker.Calls;
        var inbox = new InMemoryInbox(calls);
        await using var watcher = new StepWatcher(
            docker.Client,
            new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, Hephaestus.Core.AgentBinaries.AgentArchitecture.X64) { RetryInterval = TimeSpan.FromMilliseconds(1) },
            inbox,
            new InMemoryOutput(calls),
            new Hephaestus.Core.Secrets.SecretMasker(NSubstitute.Substitute.For<Hephaestus.Core.Ports.ISecretStore>()),
            TimeProvider.System);

        watcher.Watch(new WatchedStep(WatchRig.TaskId, WatchRig.StepRunId, WatchRig.Container, DateTimeOffset.UtcNow.AddHours(1)));
        await watcher.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), Token);

        Assert.Single(inbox.Appended);
    }

    /// <summary>A log that gives what it holds and then waits for ever, whatever token it is read with, until it is closed.</summary>
    private sealed class DeafLog(byte[] first) : Stream
    {
        private readonly MemoryStream _first = new(first);
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Closed => _closed.Task.IsCompleted;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_first.Position < _first.Length)
            {
                return _first.Read(buffer.Span);
            }

            await _closed.Task;
            throw new IOException("closed");
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        protected override void Dispose(bool disposing)
        {
            _closed.TrySetResult();
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A log that gives one line and then waits for ever, until it is told to stop.</summary>
    private sealed class EndlessLog(byte[] first, CancellationToken stop) : Stream
    {
        private readonly MemoryStream _first = new(first);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_first.Position < _first.Length)
            {
                return _first.Read(buffer.Span);
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop);
            await Task.Delay(Timeout.Infinite, linked.Token);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    [Fact]
    public async Task The_hosts_the_egress_proxy_refused_are_read_from_its_log_and_carried_on_the_Signal_each_once()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.ProxyLogs = (_, _) => Task.FromResult(DockerDouble.Output((1, "listening 3128\nrefused evil.example not-allowed\n"), (1, "refused 169.254.169.254 private-address\nrefused evil.example not-allowed\n")));

        await rig.WatchToTheEndAsync();

        var signal = Assert.Single(rig.Inbox.Appended);
        Assert.Equal(["evil.example", "169.254.169.254"], signal.RefusedHosts!);
        Assert.Equal([$"hephaestus-egress-{WatchRig.StepRunId}"], rig.Docker.ProxyLogRequests);
    }

    [Fact]
    public async Task A_proxy_that_refused_nothing_leaves_no_hosts_on_the_Signal()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));

        await rig.WatchToTheEndAsync();

        Assert.Null(Assert.Single(rig.Inbox.Appended).RefusedHosts);
    }

    [Fact]
    public async Task A_proxy_that_is_already_gone_leaves_no_hosts_and_is_not_a_problem()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.ProxyLogs = (_, _) => Task.FromException<MultiplexedStream>(DockerDouble.NotFound());

        await rig.WatchToTheEndAsync();

        Assert.Null(Assert.Single(rig.Inbox.Appended).RefusedHosts);
        Assert.Empty(rig.Problems);
    }

    [Fact]
    public async Task A_proxy_log_that_cannot_be_read_is_reported_once_and_the_step_still_ends_with_no_host_recorded()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.ProxyLogs = (_, _) => Task.FromException<MultiplexedStream>(new HttpRequestException("gone"));

        await rig.WatchToTheEndAsync();

        Assert.Null(Assert.Single(rig.Inbox.Appended).RefusedHosts);
        Assert.Single(rig.Problems, problem => problem.Contains("refused could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_proxy_or_network_that_cannot_be_removed_is_reported_and_the_rest_is_still_tried()
    {
        await using var rig = new WatchRig();
        rig.Docker.Inspector = RunsThenEnds(0, DockerDouble.Inspected("exited"));
        rig.Docker.RemoveFails = container => container.StartsWith("hephaestus-egress-", StringComparison.Ordinal) ? new HttpRequestException("busy") : null;
        rig.Docker.NetworkRemoveFails = _ => new HttpRequestException("busy");

        await rig.WatchToTheEndAsync();

        Assert.Equal(2, rig.Problems.Count(problem => problem.Contains("could not be removed", StringComparison.Ordinal)));
        Assert.Equal([$"hephaestus-context-{WatchRig.StepRunId}", $"hephaestus-methodology-{WatchRig.StepRunId}"], rig.Docker.RemovedVolumes);
        Assert.Equal([$"hephaestus-net-{WatchRig.StepRunId}"], rig.Docker.RemovedNetworks);
    }
}
