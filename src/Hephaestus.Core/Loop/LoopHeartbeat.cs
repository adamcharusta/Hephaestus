namespace Hephaestus.Core.Loop;

/// <summary>What the API reports of the orchestrator loop (AR-42): whether it is alive, and when it last ticked.</summary>
/// <param name="LastTickAt">When the loop last ticked; null when it has not ticked since the host started.</param>
public sealed record LoopHealth(bool Alive, DateTimeOffset? LastTickAt);

/// <summary>
/// The loop's pulse. The loop beats after every Signal it applies and at the end of every tick that met no failure, so a loop that is
/// stuck, or fails on every tick, stops beating, and one that is merely busy does not. This is liveness, not state: nothing needed for
/// recovery is kept here (AD-1).
/// </summary>
public sealed class LoopHeartbeat(TimeProvider time)
{
    /// <summary>How long after its last beat the loop still counts as alive: many ticks, so that one slow tick is not an alarm.</summary>
    public static readonly TimeSpan AliveWithin = TimeSpan.FromSeconds(30);

    private long _lastTickTicks;

    public void Beat() => Interlocked.Exchange(ref _lastTickTicks, time.GetUtcNow().UtcTicks);

    public LoopHealth Check()
    {
        var ticks = Interlocked.Read(ref _lastTickTicks);
        if (ticks == 0)
        {
            return new LoopHealth(Alive: false, LastTickAt: null);
        }

        var lastTick = new DateTimeOffset(ticks, TimeSpan.Zero);
        return new LoopHealth(time.GetUtcNow() - lastTick <= AliveWithin, lastTick);
    }
}
