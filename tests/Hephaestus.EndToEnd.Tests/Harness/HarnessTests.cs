using Hephaestus.Core.Aliases;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;

namespace Hephaestus.EndToEnd.Tests.Harness;

/// <summary>
/// Story 3.3, AR-37: the harness itself, which needs no Docker Engine. A real installation starts over a real SQLite database and its loop applies a Signal; the scripted
/// adapter describes an invocation that runs its script in a shell and calls no vendor.
/// </summary>
public class HarnessTests
{
    private const long Repository = 101;
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_real_installation_starts_over_real_SQLite_and_its_loop_applies_what_is_appended()
    {
        await using var installation = await Installation.StartAsync();
        await installation.RegisterProjectAsync(Repository, 29, "alice");

        await installation.Inbox.AppendAsync([new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, Repository, 7, 29, "alice", "hephaestus", null, null, Noon, "Add a login page", "Use OAuth.")], Cancel);
        var task = await Installation.EventuallyAsync(() => installation.Reads.FindTaskAsync(Repository, 7, Cancel));

        Assert.Equal(TaskState.Queued, task.State);
    }

    [Fact]
    public async Task The_end_of_a_step_that_the_runtime_reports_is_applied_by_the_loop_of_the_installation()
    {
        await using var installation = await Installation.StartAsync();
        var stepRun = Guid.CreateVersion7();

        await installation.Inbox.AppendAsync([Signal.StepExited(stepRun, 0, Hephaestus.Core.Workspaces.StepExitCause.Exited, Noon)], Cancel);
        var applied = await Installation.EventuallyAsync(async () => (await installation.Inbox.ReadAsync(0, 10, Cancel)).FirstOrDefault(stored => stored.Status == SignalStatus.Applied));

        Assert.Equal(stepRun, applied.Signal.StepRunId);
    }

    [Fact]
    public void The_scripted_adapter_describes_an_invocation_that_runs_its_script_in_a_shell_and_makes_no_vendor_call()
    {
        IAgentAdapter adapter = new ScriptedAgentAdapter("echo hello; exit 3", Agent.CodexCli);

        var invocation = adapter.DescribeInvocation(new InvocationRequest(Guid.CreateVersion7(), "instruction.md"));

        Assert.Equal(Agent.CodexCli, adapter.Agent);
        Assert.Equal(["/bin/sh", "-c", "echo hello; exit 3"], invocation.Command);
        Assert.Empty(invocation.Environment);
        Assert.Empty(invocation.EgressHosts);
        Assert.Equal("instruction.md", Assert.Single(invocation.Files).Path);
        Assert.Equal("/workspace/result.txt", invocation.ResultPath);
        Assert.DoesNotContain(invocation.Command, part => part.Contains("claude", StringComparison.OrdinalIgnoreCase) || part.Contains("codex", StringComparison.OrdinalIgnoreCase));
    }
}
