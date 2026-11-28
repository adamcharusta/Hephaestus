using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Windows;
using Hephaestus.Core.Workspaces;
using Hephaestus.EndToEnd.Tests.Harness;
using Hephaestus.Host;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.EndToEnd.Tests;

/// <summary>
/// Story 3.14, AR-37, FR-14, FR-16, FR-2: the installation of the harness, real on real SQLite with its real loop, host, control API, and command line, is given a labelled issue and the Owner's <c>project start</c>, and its scripted fake Agent drives the Task
/// to a pull request with no vendor call and no Docker Engine: the runtime, GitHub, and the Agent's process are the harness's, and everything between them is the product's.
/// </summary>
public class RunToPullRequestTests
{
    private const long Repository = 101;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private const string ConfigurationFile = "primaryAlias: opus\neffort: deep\nvalidation:\n  - dotnet build\n  - dotnet test\nimage: ubuntu:24.04\n";

    private static readonly string Completed = ScriptedAgentAdapter.ResultPrefix + """{"outcome":"completed","summary":"Added a login page.","questions":[]}""";

    private sealed record Run(Installation Installation, ScriptedGitHub GitHub, ScriptedRuntime Runtime, ScriptedAgentAdapter Adapter) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Installation.DisposeAsync();
    }

    private static async Task<Run> StartAsync(Action<ScriptedRuntime>? script = null, string configuration = ConfigurationFile)
    {
        var github = new ScriptedGitHub { Configuration = configuration };
        var adapter = new ScriptedAgentAdapter("echo the scripted Agent edits files and prints its result");
        ScriptedRuntime? runtime = null;
        var installation = await Installation.StartAsync(services: collection =>
        {
            collection.AddSingleton<ICodeHost>(github);
            collection.AddSingleton<IPullRequests>(github);
            collection.AddSingleton<ITaskBranches>(github);
            collection.AddSingleton<IRepositoryMirrors>(github);
            collection.AddSingleton<IAgentAdapter>(adapter);
            collection.AddSingleton<IWorkspaceRuntime>(provider =>
            {
                runtime = new ScriptedRuntime(provider.GetRequiredService<ISignalInbox>(), provider.GetRequiredService<IOutputStore>(), TimeProvider.System)
                {
                    Agent = _ => new ScriptedEnd(0, "the Agent worked", Completed),
                };
                script?.Invoke(runtime);
                return runtime;
            });
        });
        var configurationStore = installation.Configuration;
        var at = DateTimeOffset.UtcNow;
        await installation.RegisterProjectAsync(Repository, 29, "alice");
        await configurationStore.AddAliasAsync(Alias.ForModel("opus", Agent.ClaudeCode, "claude-opus-5-5", at), Cancel);
        await configurationStore.AddAliasAsync(Alias.ForEffort("deep", Effort.High, at), Cancel);
        await configurationStore.SetAgentAuthenticationAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, at, credentialStored: true, Cancel);
        await configurationStore.SetGitHubAppIdAsync(4711, "hephaestus-e2e", Cancel);
        installation.Services.GetRequiredService<IWorkspaceRuntime>();
        return new Run(installation, github, runtime!, adapter);
    }

    private static Signal Label(int issue = 7, string title = "Add a login page") =>
        new(Signal.GitHubSource, $"issue-event:{issue}", SignalKind.Labeled, Repository, issue, 29, "alice", "hephaestus", null, null, DateTimeOffset.UtcNow, title, "Use OAuth.");

    private static async Task<(int Exit, string Output, string Error)> CliAsync(Run run, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var environment = new Dictionary<string, string?> { [HostSettings.ControlSocketVariable] = run.Installation.ControlSocketPath };
        var exit = await EntryPoint.RunAsync(args, new StringReader(string.Empty), output, error, name => environment.GetValueOrDefault(name), Cancel);
        return (exit, output.ToString(), error.ToString());
    }

    private static async Task<TaskRecord> OptInAsync(Run run, int issue = 7)
    {
        run.GitHub.Opted(Repository, issue);
        await run.Installation.Inbox.AppendAsync([Label(issue)], Cancel);
        return await Installation.EventuallyAsync(() => run.Installation.Reads.FindTaskAsync(Repository, issue, Cancel));
    }

    /// <summary>Waits until everything the loop journaled was made: the label that shows the last state is the last of the effects of a Task.</summary>
    private static Task SettledAsync(Run run) =>
        Installation.EventuallyAsync(async () => (await run.Installation.Reads.ListUnfinishedEffectsAsync(Cancel)).Count == 0 ? new object() : null, Patience);

    private static Task<TaskRecord> UntilAsync(Run run, int issue, Func<TaskRecord, bool> reached) =>
        Installation.EventuallyAsync(async () => await run.Installation.Reads.FindTaskAsync(Repository, issue, Cancel) is { } task && reached(task) ? task : null, Patience);

    [Fact]
    public async Task A_labelled_issue_and_a_start_on_demand_give_a_pull_request_through_every_step_with_the_scripted_Agent_and_no_vendor_call()
    {
        await using var run = await StartAsync();
        var queued = await OptInAsync(run);

        var started = await CliAsync(run, "project", "start", "acme/shop");
        var task = await UntilAsync(run, 7, t => t.State == TaskState.PullRequestOpen);

        Assert.Equal((0, string.Empty), (started.Exit, started.Error));
        Assert.Contains("A window is open on acme/shop.", started.Output, StringComparison.Ordinal);
        Assert.Equal(queued.Id, task.Id);

        // The Task ran as steps: one Implementation of the Primary Agent on a fresh checkout, then one Validation container for each command of the Project.
        var steps = await run.Installation.Reads.ListStepRunsAsync(task.Id, Cancel);
        Assert.Equal([StepKind.Implementation, StepKind.Validation, StepKind.Validation], steps.Select(step => step.Kind));
        Assert.Equal([0, 1], steps.Where(step => step.IsValidation).Select(step => step.CommandIndex!.Value));
        Assert.Equal(["dotnet build", "dotnet test"], run.Runtime.Commands.Select(command => command.Command));
        var implementation = steps[0];
        Assert.Equal((Agent.ClaudeCode, "claude-opus-5-5", Effort.High, "completed", null), (implementation.Agent!.Value, implementation.Model, implementation.Effort!.Value, implementation.Outcome, implementation.Failure));
        Assert.Equal(ScriptedGitHub.BaseCommit, Assert.Single(run.Runtime.Checkouts).Bundle.Tip);
        Assert.DoesNotContain(run.Runtime.AgentSteps[0].Command, part => part.Contains("claude", StringComparison.OrdinalIgnoreCase) || part.Contains("codex", StringComparison.OrdinalIgnoreCase));

        // What the Agent printed is kept by the output store and was read from it when the step ended.
        await using (var stored = await run.Installation.Output.OpenReadAsync(implementation.Id, Cancel))
        {
            Assert.Contains("the Agent worked", await new StreamReader(stored!).ReadToEndAsync(Cancel), StringComparison.Ordinal);
        }

        // The work was pushed once, from the Task branch, and the pull request was opened once, from it to the Base Branch, under the Task's issue.
        Assert.Equal([$"{task.Branch}@{new string('b', 40)}"], run.GitHub.Pushed);
        var publication = Assert.Single(run.GitHub.Publications);
        Assert.Equal((task.Branch, "main", "Add a login page"), (publication.Head, publication.Base, publication.Title));
        Assert.Contains("Added a login page.", publication.Body, StringComparison.Ordinal);
        Assert.Contains("dotnet test", publication.Body, StringComparison.Ordinal);
        Assert.Equal(1, run.GitHub.OpenPullRequests(task.Branch));

        // Its state was shown on the issue as it moved, and the window closed when the Queue was empty.
        await SettledAsync(run);
        Assert.Contains("hephaestus:pull-request-open", run.GitHub.LabelsOf(Repository, 7));
        Assert.DoesNotContain("hephaestus:running", run.GitHub.LabelsOf(Repository, 7));
        Assert.Contains(run.GitHub.LabelHistory, labels => labels.Contains("hephaestus:running"));
        var windows = await run.Installation.Reads.ListWindowsAsync(openOnly: false, Cancel);
        Assert.Equal(WindowEnd.Drained, Assert.Single(windows).End);
        Assert.Empty(await run.Installation.Reads.ListUnfinishedEffectsAsync(Cancel));
    }

    [Fact]
    public async Task The_Signal_of_the_start_is_read_back_as_applied_through_the_command_line_and_the_Task_it_started_ends_in_a_pull_request()
    {
        await using var run = await StartAsync(script: runtime => runtime.Agent = _ => new ScriptedEnd(0, Completed));
        await OptInAsync(run);

        var first = await CliAsync(run, "project", "start", "acme/shop", "--json");
        var shown = await CliAsync(run, "signal", "show", System.Text.Json.JsonDocument.Parse(first.Output).RootElement.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(0, first.Exit);
        Assert.Contains("\"status\":\"applied\"", first.Output, StringComparison.Ordinal);
        Assert.Equal(0, shown.Exit);
        Assert.Contains("status: applied", shown.Output, StringComparison.Ordinal);
        await UntilAsync(run, 7, t => t.State == TaskState.PullRequestOpen);
        Assert.Single(await run.Installation.Reads.ListWindowsAsync(openOnly: false, Cancel));
    }

    [Fact]
    public async Task A_step_that_does_not_end_well_ends_the_Task_as_Failed_with_no_pull_request_and_frees_the_slot_for_the_next_Task()
    {
        await using var run = await StartAsync(script: runtime =>
        {
            runtime.Agent = request => request.Context.Any(file => file.Path == "issue.md" && System.Text.Encoding.UTF8.GetString(file.Content).Contains("Breaks", StringComparison.Ordinal))
                ? new ScriptedEnd(3, "it broke")
                : new ScriptedEnd(0, Completed);
        });
        run.GitHub.Opted(Repository, 3);
        run.GitHub.Opted(Repository, 9);
        await run.Installation.Inbox.AppendAsync([Label(3, "Breaks"), Label(9, "Works")], Cancel);
        await Installation.EventuallyAsync(() => run.Installation.Reads.FindTaskAsync(Repository, 9, Cancel));

        await CliAsync(run, "project", "start", "acme/shop");
        var failed = await UntilAsync(run, 3, t => t.State == TaskState.Failed);
        var second = await UntilAsync(run, 9, t => t.State == TaskState.PullRequestOpen);

        await SettledAsync(run);
        Assert.Equal(TaskState.Failed, failed.State);
        Assert.Equal(second.Branch, Assert.Single(run.GitHub.Publications).Head);
        var broken = Assert.Single(await run.Installation.Reads.ListStepRunsAsync(failed.Id, Cancel));
        Assert.Equal(("unknown", 3), (broken.Failure, broken.ExitCode));
        Assert.Contains("hephaestus:failed", run.GitHub.LabelsOf(Repository, 3));
    }

    [Fact]
    public async Task A_stop_closes_the_window_and_the_Queue_waits_until_the_Owner_starts_again()
    {
        await using var run = await StartAsync();
        await OptInAsync(run, issue: 4);
        var stoppedBeforeStart = await CliAsync(run, "project", "stop", "acme/shop");
        await Task.Delay(TimeSpan.FromSeconds(2.5), Cancel);

        var queued = await run.Installation.Reads.FindTaskAsync(Repository, 4, Cancel);

        Assert.Equal(0, stoppedBeforeStart.Exit);
        Assert.Contains("No window is open on acme/shop; nothing was closed.", stoppedBeforeStart.Output, StringComparison.Ordinal);
        Assert.Equal(TaskState.Queued, queued!.State);
        Assert.Empty(run.Runtime.AgentSteps);
    }
}
