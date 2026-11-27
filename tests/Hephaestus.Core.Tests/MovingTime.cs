namespace Hephaestus.Core.Tests;

/// <summary>A clock a test moves.</summary>
internal sealed class MovingTime(DateTimeOffset now) : TimeProvider
{
    public MovingTime()
        : this(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public DateTimeOffset Now { get; set; } = now;

    public void Advance(TimeSpan by) => Now += by;

    public override DateTimeOffset GetUtcNow() => Now;
}
