using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// A secret store in memory that records which names were read and whether every value was, for the tests of what the runtime resolves and when. It has the masker that goes with
/// it, rebuilt by <see cref="ReadyAsync"/> after the values are set.
/// </summary>
internal sealed class TestSecrets : ISecretStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public TestSecrets() => Masker = new SecretMasker(this);

    public SecretMasker Masker { get; }

    /// <summary>The names <see cref="ReadAsync"/> was asked for, in order.</summary>
    public List<string> Reads { get; } = [];

    /// <summary>How many times every value was asked for, which only the masker does.</summary>
    public int ReadAlls { get; private set; }

    public event Func<Task>? Changed
    {
        add { }
        remove { }
    }

    public TestSecrets With(string name, string value)
    {
        _values[name] = value;
        return this;
    }

    /// <summary>Teaches the masker the values that are stored now, and forgets the reads that took.</summary>
    public async Task ReadyAsync()
    {
        await Masker.RebuildAsync(CancellationToken.None);
        ReadAlls = 0;
    }

    public Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        _values[name] = value;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string name, CancellationToken cancellationToken) => Task.FromResult(_values.Remove(name));

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        Reads.Add(name);
        return Task.FromResult(_values.GetValueOrDefault(name));
    }

    public Task<IReadOnlyList<string>> ReadAllAsync(CancellationToken cancellationToken)
    {
        ReadAlls++;
        return Task.FromResult<IReadOnlyList<string>>([.. _values.Values]);
    }
}

/// <summary>Makes the runtime under test with a secret store that holds nothing, for the tests that start no invocation.</summary>
internal static class Runtimes
{
    public static DockerWorkspaceRuntime Of(Docker.DotNet.IDockerClient docker, WorkspaceRuntimeOptions options, IStepWatcher watcher, TestSecrets? secrets = null)
    {
        secrets ??= new TestSecrets();
        return new DockerWorkspaceRuntime(docker, options, watcher, secrets, secrets.Masker);
    }
}
