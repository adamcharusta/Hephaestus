using Hephaestus.Core.Loop;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>AR-42: the API reports that the loop is alive and when its last tick ran.</summary>
public class LoopHeartbeatTests
{
    [Fact]
    public void Before_the_first_tick_the_loop_is_not_alive_and_has_no_last_tick()
    {
        var heartbeat = new LoopHeartbeat(new MovingTime());

        Assert.Equal(new LoopHealth(false, null), heartbeat.Check());
    }

    [Fact]
    public void A_loop_that_beat_recently_is_alive_and_reports_when()
    {
        var clock = new MovingTime(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
        var heartbeat = new LoopHeartbeat(clock);

        heartbeat.Beat();
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(new LoopHealth(true, new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero)), heartbeat.Check());
    }

    [Fact]
    public void A_loop_that_stopped_beating_is_no_longer_alive_but_its_last_tick_is_still_reported()
    {
        var clock = new MovingTime(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
        var heartbeat = new LoopHeartbeat(clock);
        heartbeat.Beat();

        clock.Advance(LoopHeartbeat.AliveWithin);
        Assert.True(heartbeat.Check().Alive);

        clock.Advance(TimeSpan.FromSeconds(1));
        var health = heartbeat.Check();

        Assert.False(health.Alive);
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero), health.LastTickAt);
    }

    [Fact]
    public void A_new_beat_makes_a_stopped_loop_alive_again()
    {
        var clock = new MovingTime(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
        var heartbeat = new LoopHeartbeat(clock);
        heartbeat.Beat();
        clock.Advance(TimeSpan.FromMinutes(5));

        heartbeat.Beat();

        Assert.True(heartbeat.Check().Alive);
    }
}
