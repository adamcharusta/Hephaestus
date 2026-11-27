using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Output;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Workspaces;
using Hephaestus.Host;
using Hephaestus.Host.Hosting;
using Hephaestus.Workspaces;
using Hephaestus.Workspaces.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hephaestus.EndToEnd.Tests.Harness;

/// <summary>A step run the harness started: the Task and step run it belongs to, and its container.</summary>
internal sealed record HarnessStep(Guid TaskId, Guid StepRunId, string ContainerId);

/// <summary>
/// The end-to-end harness (AR-37, Story 3.3): a real installation, which is the real host with its loop, over a real SQLite database in a directory of its own, and, when
/// an <see cref="EngineFixture"/> is given, the real Docker Engine. A step is described by a <see cref="ScriptedAgentAdapter"/> and run in a step container; no vendor is called.
/// Every later story of the epic adds its behavior here, so that integration grows story by story. Disposing stops the host and removes the directory; the Docker objects
/// carry the fixture's installation label, which the fixture removes by.
/// </summary>
internal sealed class Installation : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    private readonly string _directory;

    private Installation(string directory, WebApplication app, EngineFixture? engine)
    {
        _directory = directory;
        App = app;
        Engine = engine;
    }

    public WebApplication App { get; }

    public EngineFixture? Engine { get; }

    public IServiceProvider Services => App.Services;

    public ISignalInbox Inbox => Services.GetRequiredService<ISignalInbox>();

    public IReadStore Reads => Services.GetRequiredService<IReadStore>();

    public IOutputStore Output => Services.GetRequiredService<IOutputStore>();

    public IInstallationConfiguration Configuration => Services.GetRequiredService<IInstallationConfiguration>();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>Starts the installation: migrated, listening on its control socket, its loop and poller running.</summary>
    public static async Task<Installation> StartAsync(EngineFixture? engine = null)
    {
        var directory = Directory.CreateTempSubdirectory("hxe").FullName;
        var agents = AgentsEnvironment.Prepare(directory);
        var settings = new HostSettings(
            Path.Combine(directory, "control.sock"),
            Language.English,
            Path.Combine(directory, "data"),
            Path.Combine(directory, "secrets"),
            agents["HEPHAESTUS_AGENTS_DIR"],
            agents["HEPHAESTUS_AGENTS_LOCK"],
            agents["HEPHAESTUS_CA_BUNDLE"],
            engine?.InstallationId ?? HostSettings.DefaultInstallationId);
        var app = ControlHost.Build(settings, builder =>
        {
            if (engine is not null)
            {
                // The fixture's own network, agents volume, and installation id, so that the installation and the Engine's objects belong together. Short waits, so that a test
                // that stops a step does not wait for the production defaults.
                builder.Services.TryAddSingleton(new WorkspaceRuntimeOptions(engine.InstallationId, engine.Vendor.Lock, engine.HostArchitecture)
                {
                    AgentsVolume = engine.AgentsVolume,
                    OutboundNetwork = engine.OutboundNetwork,
                    EgressProxyImage = engine.ProxyImage ?? "hephaestus.invalid/none:0",
                    StepPollInterval = TimeSpan.FromMilliseconds(250),
                    StopGraceSeconds = 1,
                });
            }
        });
        try
        {
            await ControlHost.MigrateAsync(app, Cancel);
            await app.StartAsync(Cancel);
        }
        catch
        {
            await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
            throw;
        }

        return new Installation(directory, app, engine);
    }

    /// <summary>Makes a Trusted User and a registered Project, so that a Marker Label event of the user makes a Task.</summary>
    public async Task RegisterProjectAsync(long repositoryId, long trustedUserId, string trustedLogin)
    {
        await Configuration.AddTrustedUserAsync(trustedUserId, trustedLogin, Cancel);
        await Configuration.RegisterProjectAsync(repositoryId, "acme/shop", "main", Cancel);
    }

    /// <summary>Describes the scripted Agent's invocation of a step, creates the Task's volume, and starts the step's container. Returns as soon as the container is started, as the runtime does.</summary>
    public async Task<HarnessStep> StartStepAsync(ScriptedAgentAdapter adapter, TimeSpan timeout, string image = EngineFixture.Musl)
    {
        // A step needs its egress proxy, which is the hephaestus image: where none is named, the test proves nothing, and in continuous integration that is a failure.
        if (Engine?.ProxyImage is null)
        {
            var reason = $"{EngineFixture.ProxyImageVariable} names no hephaestus image to create the egress proxies from.";
            if (RealEngine.Required)
            {
                Assert.Fail(reason);
            }

            Assert.Skip(reason);
        }

        var runtime = Services.GetRequiredService<IWorkspaceRuntime>();
        var time = TimeProvider.System;
        var taskId = EntityId.New(time);
        var stepRunId = EntityId.New(time);
        var invocation = adapter.DescribeInvocation(new InvocationRequest(stepRunId, "instruction.md", AuthenticationMode.ApiKey, "scripted"));
        var resolved = await runtime.ResolveImageAsync(image, Cancel);
        await runtime.EnsureTaskVolumeAsync(taskId, resolved, $"task:{taskId:N}:volume:0", Cancel);
        var started = await runtime.StartStepAsync(
            new StepContainerRequest(taskId, stepRunId, resolved, invocation.Command, invocation.Environment, new StepLimits(1000, 256L * 1024 * 1024), invocation.Files, [], time.GetUtcNow() + timeout, AllowedEgressHosts: null),
            $"task:{taskId:N}:container:0",
            Cancel);
        return new HarnessStep(taskId, stepRunId, started.ContainerId);
    }

    /// <summary>Waits until the loop has applied the step-exited Signal of the step run, and returns it as the inbox holds it.</summary>
    public Task<StoredSignal> WaitForTheEndAsync(HarnessStep step) =>
        EventuallyAsync(async () => (await Inbox.ReadAsync(0, 1000, Cancel)).FirstOrDefault(stored => stored.Signal.StepRunId == step.StepRunId && stored.Status == SignalStatus.Applied), Patience);

    /// <summary>The stored output of the step run, as text; null when none was kept.</summary>
    public async Task<string?> ReadOutputAsync(HarnessStep step)
    {
        await using var stream = await Output.OpenReadAsync(step.StepRunId, Cancel);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(Cancel);
    }

    /// <summary>Whether the Engine still holds the container of the step run, or a volume the runtime made for it.</summary>
    public async Task<bool> EngineHoldsObjectsOfAsync(HarnessStep step)
    {
        var docker = Engine!.Docker;
        var label = new Dictionary<string, IDictionary<string, bool>> { ["label"] = new Dictionary<string, bool> { [$"hephaestus.step-run={step.StepRunId:D}"] = true } };
        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true, Filters = label }, Cancel);
        var volumes = (await docker.Volumes.ListAsync(new VolumesListParameters { Filters = label }, Cancel)).Volumes ?? [];
        return containers.Count > 0 || volumes.Count > 0;
    }

    public async Task<bool> ContainerIsRunningAsync(HarnessStep step)
    {
        var inspected = await Engine!.Docker.Containers.InspectContainerAsync(step.ContainerId, Cancel);
        return inspected.State?.Running == true;
    }

    public static async Task<T> EventuallyAsync<T>(Func<Task<T?>> read, TimeSpan? within = null)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow + (within ?? TimeSpan.FromSeconds(15));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await read() is { } found)
            {
                return found;
            }

            await Task.Delay(50, Cancel);
        }

        throw new TimeoutException("What the test waited for did not happen in time.");
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }
}
