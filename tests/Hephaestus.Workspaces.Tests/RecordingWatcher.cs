namespace Hephaestus.Workspaces.Tests;

/// <summary>A watcher that only records what the runtime hands it, for the tests of what the runtime does before the watching begins.</summary>
internal sealed class RecordingWatcher : IStepWatcher
{
    public List<WatchedStep> Watched { get; } = [];

    public void Watch(WatchedStep step) => Watched.Add(step);
}
