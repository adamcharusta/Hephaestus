using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Tests.Secrets;

/// <summary>An <see cref="ISecretStore"/> in memory that raises <see cref="Changed"/> the way the real one does: after a change, awaited.</summary>
internal sealed class FakeSecretStore(params string[] values) : ISecretStore
{
    private readonly Dictionary<string, string> _values = values.Select((value, index) => (value, index)).ToDictionary(pair => $"secret-{pair.index}", pair => pair.value);

    public event Func<Task>? Changed;

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        _values[name] = value;
        await RaiseAsync();
    }

    public async Task<bool> RemoveAsync(string name, CancellationToken cancellationToken)
    {
        var removed = _values.Remove(name);
        if (removed)
        {
            await RaiseAsync();
        }

        return removed;
    }

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken) => Task.FromResult(_values.GetValueOrDefault(name));

    public Task<IReadOnlyList<string>> ReadAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([.. _values.Values]);

    private Task RaiseAsync() => Changed?.Invoke() ?? Task.CompletedTask;
}
