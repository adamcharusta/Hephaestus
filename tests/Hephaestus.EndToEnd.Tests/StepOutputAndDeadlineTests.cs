using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;
using Hephaestus.EndToEnd.Tests.Harness;
using Hephaestus.Workspaces.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.EndToEnd.Tests;

/// <summary>
/// Story 3.3 end to end (AD-18, FR-4, FR-19, FR-53, AR-37): an installation on real SQLite and the real Docker Engine runs steps that a scripted Agent describes and makes no vendor call.
/// What a step printed is kept, masked, in one file; its end is reported once, with the runtime's own cause, and applied by the loop; its container is removed; a step that outlives
/// its deadline is stopped by the runtime and keeps its output; and the loop applies other Signals while a step runs. The tests are skipped where no Engine answers and run in CI.
/// </summary>
public sealed class StepOutputAndDeadlineTests(EngineFixture engine) : IClassFixture<EngineFixture>
{
    private const string Skipped = "No Docker Engine answers on this machine.";
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_Docker_Engine_answers_wherever_the_tests_that_need_one_are_required_to_run()
    {
        Assert.True(RealEngine.Available || !RealEngine.Required, "GitHub Actions must be able to reach a Docker Engine: the end-to-end tests of steps would otherwise be skipped.");
    }

    [Fact(Skip = Skipped, SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_step_that_prints_and_exits_leaves_its_output_in_one_file_one_Signal_the_loop_applied_and_no_object_of_its_own_in_Docker()
    {
        await using var installation = await Installation.StartAsync(engine);

        var step = await installation.StartStepAsync(new ScriptedAgentAdapter("echo to-stdout; sleep 1; echo to-stderr >&2; exit 3"), TimeSpan.FromMinutes(5));
        var ended = await installation.WaitForTheEndAsync(step);

        Assert.Equal((SignalKind.StepExited, step.StepRunId, 3, StepExitCause.Exited), (ended.Signal.Kind, ended.Signal.StepRunId, ended.Signal.ExitCode, ended.Signal.Cause));
        Assert.Equal((Signal.RuntimeSource, step.StepRunId.ToString("D")), (ended.Signal.Source, ended.Signal.SourceEventId));
        var output = await installation.ReadOutputAsync(step);
        Assert.NotNull(output);
        Assert.Contains("to-stdout", output, StringComparison.Ordinal);
        Assert.Contains("to-stderr", output, StringComparison.Ordinal);
        Assert.True(output.IndexOf("to-stdout", StringComparison.Ordinal) < output.IndexOf("to-stderr", StringComparison.Ordinal));
        var stored = await installation.Output.FindAsync(step.StepRunId, TestContext.Current.CancellationToken);
        Assert.True(stored!.Complete);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(output), stored.Bytes);
        Assert.False(await installation.EngineHoldsObjectsOfAsync(step));
    }

    [Fact(Skip = Skipped, SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_secret_in_a_steps_output_is_masked_before_it_is_stored_even_when_it_arrives_in_two_pieces()
    {
        await using var installation = await Installation.StartAsync(engine);
        await installation.Services.GetRequiredService<ISecretStore>().SetAsync("end-to-end-secret", "sk-e2e-0123456789-abcdefghij", TestContext.Current.CancellationToken);

        var step = await installation.StartStepAsync(new ScriptedAgentAdapter("printf 'token=sk-e2e-0123456'; sleep 1; printf '789-abcdefghij done\\n'"), TimeSpan.FromMinutes(5));
        await installation.WaitForTheEndAsync(step);

        var output = await installation.ReadOutputAsync(step);
        Assert.Equal("token=*** done\n", output);
    }

    [Fact(Skip = Skipped, SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_step_that_outlives_its_deadline_is_stopped_by_the_runtime_ends_with_the_cause_timeout_and_keeps_what_it_printed()
    {
        await using var installation = await Installation.StartAsync(engine);

        var step = await installation.StartStepAsync(new ScriptedAgentAdapter("echo started; sleep 600"), TimeSpan.FromSeconds(4));
        var ended = await installation.WaitForTheEndAsync(step);

        Assert.Equal(StepExitCause.Timeout, ended.Signal.Cause);
        Assert.NotEqual(0, ended.Signal.ExitCode);
        Assert.Equal("started\n", await installation.ReadOutputAsync(step));
        Assert.True((await installation.Output.FindAsync(step.StepRunId, TestContext.Current.CancellationToken))!.Complete);
        Assert.False(await installation.EngineHoldsObjectsOfAsync(step));
    }

    [Fact(Skip = Skipped, SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_loop_applies_a_Signal_within_a_tick_while_a_step_is_still_running_because_it_never_waits_for_the_step()
    {
        await using var installation = await Installation.StartAsync(engine);
        await installation.RegisterProjectAsync(101, 29, "alice");

        var step = await installation.StartStepAsync(new ScriptedAgentAdapter("echo running; sleep 600"), TimeSpan.FromMinutes(10));
        await installation.Inbox.AppendAsync([new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, 101, 7, 29, "alice", "hephaestus", null, null, Noon, "Add a login page", "Use OAuth.")], TestContext.Current.CancellationToken);
        var task = await Installation.EventuallyAsync(() => installation.Reads.FindTaskAsync(101, 7, TestContext.Current.CancellationToken));

        Assert.Equal(TaskState.Queued, task.State);
        Assert.True(await installation.ContainerIsRunningAsync(step));
        Assert.False((await installation.Output.FindAsync(step.StepRunId, TestContext.Current.CancellationToken))?.Complete ?? false);
    }
}
