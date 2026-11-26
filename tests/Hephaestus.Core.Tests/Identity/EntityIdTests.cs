using Hephaestus.Core.Identity;

namespace Hephaestus.Core.Tests.Identity;

/// <summary>AR-30: entity ids are UUID version 7, and the time inside one is the clock's.</summary>
public class EntityIdTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void A_new_id_is_version_7_and_carries_the_time_of_the_clock()
    {
        var now = new DateTimeOffset(2026, 11, 26, 21, 30, 5, TimeSpan.Zero);

        var id = EntityId.New(new Clock(now));

        Assert.Equal(7, id.Version);
        Assert.True(EntityId.IsValid(id));
        var milliseconds = Convert.ToInt64(id.ToString("N")[..12], 16);
        Assert.Equal(now.ToUnixTimeMilliseconds(), milliseconds);
    }

    [Fact]
    public void An_id_of_another_version_is_not_valid()
    {
        Assert.False(EntityId.IsValid(Guid.NewGuid()));
        Assert.False(EntityId.IsValid(Guid.Empty));
    }

    [Fact]
    public void Later_ids_sort_after_earlier_ones_as_text()
    {
        var first = EntityId.New(new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))).ToString();
        var second = EntityId.New(new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero))).ToString();

        Assert.True(string.CompareOrdinal(first, second) < 0);
    }
}
