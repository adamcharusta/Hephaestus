using System.Text;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Failures;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Loop;
using Hephaestus.Core.Methodologies;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Readiness;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>The runtime as the loop asks it, in memory: what it was asked for, and the failures a test makes it have. A container never ends by itself; the test appends the step-exited Signal, as the watcher of the real runtime does.</summary>
internal sealed class FakeRuntime : IWorkspaceRuntime
{
    public static readonly ResolvedImage Image = new("ubuntu:24.04", "sha256:" + new string('1', 64), Hephaestus.Core.AgentBinaries.AgentArchitecture.X64, CLibrary.Glibc);

    public List<string> Images { get; } = [];

    public List<(Guid TaskId, string Key)> Volumes { get; } = [];

    public List<(CheckoutRequest Request, string Key)> Checkouts { get; } = [];

    public List<(StepContainerRequest Request, string Key)> Steps { get; } = [];

    public List<(CheckpointRequest Request, string Key)> Checkpoints { get; } = [];

    public List<(ValidationCommandRequest Request, string Key)> Validations { get; } = [];

    /// <summary>The tip the next checkpoint makes; a test sets what it wants to push.</summary>
    public string NextTip { get; set; } = new string('b', 40);

    /// <summary>Whether the next checkpoint finds nothing changed.</summary>
    public bool Unchanged { get; set; }

    /// <summary>What a method throws, by name.</summary>
    public Dictionary<string, Exception> Throws { get; } = [];

    private void Maybe(string method)
    {
        if (Throws.TryGetValue(method, out var exception))
        {
            throw exception;
        }
    }

    public Task<ResolvedImage> ResolveImageAsync(string image, CancellationToken cancellationToken)
    {
        Images.Add(image);
        Maybe(nameof(ResolveImageAsync));
        return Task.FromResult(Image with { Reference = image });
    }

    public Task EnsureTaskVolumeAsync(Guid taskId, ResolvedImage image, string idempotencyKey, CancellationToken cancellationToken)
    {
        Volumes.Add((taskId, idempotencyKey));
        Maybe(nameof(EnsureTaskVolumeAsync));
        return Task.CompletedTask;
    }

    public Task<StartedStep> StartStepAsync(StepContainerRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        Steps.Add((request, idempotencyKey));
        Maybe(nameof(StartStepAsync));
        return Task.FromResult(new StartedStep("container-" + Steps.Count, "hephaestus-step", Created: true));
    }

    public Task CheckoutAsync(CheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        Checkouts.Add((request, idempotencyKey));
        Maybe(nameof(CheckoutAsync));
        return Task.CompletedTask;
    }

    public Task<CheckpointResult> CheckpointAsync(CheckpointRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        Checkpoints.Add((request, idempotencyKey));
        Maybe(nameof(CheckpointAsync));
        return Task.FromResult(Unchanged
            ? CheckpointResult.Unchanged
            : CheckpointResult.Made(new BundleReference(BundleReference.CheckpointPath(request.TaskId, request.StepRunId), new string('c', 64), NextTip, request.Parent.Tip)));
    }

    public Task<StartedStep> StartValidationAsync(ValidationCommandRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        Validations.Add((request, idempotencyKey));
        Maybe(nameof(StartValidationAsync));
        return Task.FromResult(new StartedStep("validation-" + Validations.Count, "hephaestus-validation", Created: true));
    }

    public Task<ProbeResult> RunProbeAsync(ProbeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>The bare mirrors as the loop asks them: every commit is there, and a bundle is named for its step run.</summary>
internal sealed class FakeMirrors : IRepositoryMirrors
{
    public List<long> Fetches { get; } = [];

    public List<BundleRequest> Bundles { get; } = [];

    public MirrorFailure FetchFails { get; set; }

    public MirrorFailure BundleFails { get; set; }

    public Task<MirrorResult> FetchAsync(long repositoryId, CancellationToken cancellationToken)
    {
        Fetches.Add(repositoryId);
        return Task.FromResult(FetchFails == MirrorFailure.None ? MirrorResult.Fetched : MirrorResult.Failed(FetchFails));
    }

    public Task<BundleResult> CreateBundleAsync(BundleRequest request, CancellationToken cancellationToken)
    {
        Bundles.Add(request);
        return Task.FromResult(BundleFails == MirrorFailure.None
            ? BundleResult.Made(new BundleReference($"{request.TaskId:D}/{request.StepRunId:D}.bundle", new string('d', 64), request.Commit))
            : BundleResult.Failed(BundleFails));
    }
}

/// <summary>The Methodologies the installation ships: <c>default</c>, with its template for an implementation.</summary>
internal sealed class FakeMethodologies : IMethodologyCatalog
{
    public IReadOnlyList<string> ListNames() => ["default"];

    public Task<Methodology?> ReadAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Methodology?>(
        name == "default" ? new Methodology("default", [new WorkspaceFile("manifest.yml", "name: default\n"u8.ToArray()), new WorkspaceFile("implementation.md", "Read /hephaestus/context/task.json.\n"u8.ToArray())]) : null);
}

/// <summary>The stored output of the step runs, as the runtime's writer leaves it.</summary>
internal sealed class FakeOutput : IOutputStore
{
    public Dictionary<Guid, string[]> Lines { get; } = [];

    public Task<Hephaestus.Core.Output.StoredOutput?> FindAsync(Guid stepRunId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Stream?> OpenReadAsync(Guid stepRunId, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(Lines.TryGetValue(stepRunId, out var lines) ? new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n")) : null);

    public Task<IOutputWriter> BeginAsync(Hephaestus.Core.Output.OutputRun run, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>An adapter that describes an invocation that calls no vendor, and reads a result from a line that begins <c>RESULT:</c>.</summary>
internal sealed class RunAdapter(Agent agent = Agent.ClaudeCode) : IAgentAdapter
{
    public Agent Agent { get; } = agent;

    public string PinnedVersion => "1.0.0";

    public bool SupportsEverything { get; set; } = true;

    public FailureClass? Class { get; set; }

    /// <summary>When set, reading the stored stream fails with it.</summary>
    public Exception? ParseThrows { get; set; }

    public List<InvocationRequest> Invocations { get; } = [];

    public Support SupportsAuthentication(AuthenticationMode mode) => SupportsEverything ? Support.Yes : Support.No(new Hephaestus.Core.Localization.Message("x"));

    public Support SupportsEffort(Effort effort) => SupportsEverything ? Support.Yes : Support.No(new Hephaestus.Core.Localization.Message("x"));

    public AgentInvocation DescribeInvocation(InvocationRequest request)
    {
        Invocations.Add(request);
        return new AgentInvocation(["/hephaestus/agents/fake/fake", "run"], new Dictionary<string, string> { ["FAKE"] = "1" }, [new SecretReference("fake-api-key", "FAKE_API_KEY")], [], ["api.fake.test"], ResultLocation.InOutputStream);
    }

    public AgentInvocation DescribeProbe(AuthenticationMode mode) => throw new NotSupportedException();

    public AgentOutput Parse(IEnumerable<string> lines)
    {
        if (ParseThrows is not null)
        {
            throw ParseThrows;
        }

        var result = lines.FirstOrDefault(line => line.StartsWith("RESULT:", StringComparison.Ordinal))?[7..];
        return new AgentOutput([], result is null ? null : new AgentResult(false, null, result, "model", null, null), [], 0);
    }

    public FailureClass? ClassifyExit(int exitCode, AgentOutput output) => Class ?? (exitCode == 0 ? null : FailureClass.Unknown);
}

/// <summary>
/// An installation in memory with everything a Task needs to run from a labelled issue to a pull request: a registered Project with a valid configuration file, a Trusted User, the Aliases it names, an Agent that signs in, and fakes of the runtime, the mirrors, the branches, and
/// the pull requests. A test appends Signals and ticks the loop, and says when a container ended.
/// </summary>
internal sealed class RunRig
{
    public const long RepositoryId = LoopStores.RepositoryId;
    public const int Issue = 7;
    public static readonly string StartCommit = new('a', 40);

    public const string ConfigurationFile = """
        primaryAlias: opus
        effort: deep
        validation:
          - dotnet build
          - dotnet test
        image: ubuntu:24.04
        """;

    public RunRig(string? configuration = ConfigurationFile)
    {
        Time = new MovingTime();
        Stores = new LoopStores { TimeProvider = Time, AppSlug = "hephaestus-test" };
        var at = Time.Now;
        Stores.Aliases.AddRange([Alias.ForModel("opus", Agent.ClaudeCode, "claude-opus-5-5", at), Alias.ForEffort("deep", Effort.High, at)]);
        Stores.AgentSettings.Add(new AgentSetting(Agent.ClaudeCode, AuthenticationMode.ApiKey, at));
        if (configuration is not null)
        {
            SetConfiguration(RepositoryId, configuration, StartCommit);
        }

        Branches = new FakeBranches(Stores);
        Loop = NewLoop();
    }

    public MovingTime Time { get; }

    public LoopStores Stores { get; }

    public FakeRuntime Runtime { get; } = new();

    public FakeMirrors Mirrors { get; } = new();

    public FakeOutput Output { get; } = new();

    public RunAdapter Adapter { get; } = new();

    public FakeBranches Branches { get; }

    public FakePullRequests Pulls { get; } = new();

    public OrchestratorLoop Loop { get; }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    private OrchestratorLoop NewLoop() => new(
        Stores,
        Stores,
        Stores,
        Stores.Github,
        Stores.Notifier,
        Time,
        new LoopHeartbeat(Time),
        branches: Branches,
        pullRequests: Pulls,
        runtime: Runtime,
        mirrors: Mirrors,
        methodologies: new FakeMethodologies(),
        output: Output,
        adapters: [Adapter]);

    public void SetConfiguration(long repositoryId, string text, string commitId) =>
        Stores.Github.ConfigurationFiles[repositoryId] = GitHubFileResult.Read(new GitHubFile(Encoding.UTF8.GetBytes(text), commitId));

    /// <summary>Opts the issue in and applies it: the Task is <c>Queued</c>.</summary>
    public async Task<TaskRecord> QueueAsync(string eventId = "issue-event:1", int issue = Issue, string title = "Add a login page")
    {
        await Stores.AppendAsync([LoopStores.Labeled(eventId, issue: issue, title: title)], Token);
        await Loop.TickAsync(Token);
        return (await Stores.FindTaskAsync(RepositoryId, issue, Token))!;
    }

    public Task<long> StartAsync(long repositoryId = RepositoryId) => Stores.AppendOneAsync(Signal.Start(repositoryId, Guid.NewGuid(), Time.Now), Token);

    public Task<long> StopAsync(long repositoryId = RepositoryId) => Stores.AppendOneAsync(Signal.Stop(repositoryId, Guid.NewGuid(), Time.Now), Token);

    public async Task<TickReport> TickAsync(int times = 1)
    {
        TickReport report = null!;
        for (var tick = 0; tick < times; tick++)
        {
            report = await Loop.TickAsync(Token);
        }

        return report;
    }

    public TaskRecord Task(int issue = Issue) => Stores.Tasks.Single(t => t.IssueNumber == issue);

    public IReadOnlyList<StepRun> Steps(int issue = Issue) => [.. Stores.StepRuns.Where(s => s.TaskId == Task(issue).Id).OrderBy(s => s.Sequence)];

    public StepRun LastStep(int issue = Issue) => Steps(issue)[^1];

    /// <summary>The runtime's watcher: the container of the step run ended, with the output it left, and the Signal that says so is appended.</summary>
    public async Task ExitAsync(Guid stepRunId, int exitCode = 0, StepExitCause cause = StepExitCause.Exited, params string[] output)
    {
        if (output.Length > 0)
        {
            Output.Lines[stepRunId] = output;
        }

        await Stores.AppendAsync([Signal.StepExited(stepRunId, cause == StepExitCause.Exited ? exitCode : null, cause, Time.Now)], Token);
    }

    public static string Completed(string summary = "Added a login page.") => "RESULT:" + $$"""{"outcome":"completed","summary":"{{summary}}","questions":[]}""";

    public static string NeedsDecision() => "RESULT:" + """{"outcome":"needs-decision","summary":"Not sure.","questions":[{"question":"Which database?","options":["Postgres","SQLite"]}]}""";

    /// <summary>Runs the Task as far as its Implementation ended with the result given, its work is checkpointed, and the push is made; Validation starts in the next tick.</summary>
    public async Task<StepRun> ImplementAsync(params string[] output)
    {
        await QueueAsync();
        await StartAsync();
        await TickAsync();
        var implementation = LastStep();
        await ExitAsync(implementation.Id, 0, StepExitCause.Exited, output.Length > 0 ? output : [Completed()]);
        await TickAsync();
        return implementation;
    }

    /// <summary>Ends the Validation command that runs now, with the exit code given, and ticks.</summary>
    public async Task EndValidationAsync(int exitCode = 0, int issue = Issue, StepExitCause cause = StepExitCause.Exited)
    {
        await ExitAsync(LastStep(issue).Id, exitCode, cause);
        await TickAsync(2);
    }
}
