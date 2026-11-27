namespace Hephaestus.Workspaces.Tests;

/// <summary>A clock a test moves.</summary>
internal sealed class MovingTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public void Advance(TimeSpan by) => Now += by;

    public override DateTimeOffset GetUtcNow() => Now;
}
