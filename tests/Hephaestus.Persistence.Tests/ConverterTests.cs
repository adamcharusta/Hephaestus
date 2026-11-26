using Hephaestus.Core.Identity;
using Hephaestus.Persistence.Model;

namespace Hephaestus.Persistence.Tests;

/// <summary>AR-30 and AR-32: ids are UUID version 7 as text, and every instant is fixed-width UTC text through one converter.</summary>
public class ConverterTests
{
    [Fact]
    public void An_instant_is_stored_as_utc_text_of_a_fixed_width_whatever_its_offset()
    {
        var local = new DateTimeOffset(2026, 11, 26, 23, 30, 5, TimeSpan.FromHours(2));
        var utc = new DateTimeOffset(2026, 11, 26, 21, 30, 5, 120, TimeSpan.Zero);

        Assert.Equal("2026-11-26T21:30:05.0000000Z", UtcInstantConverter.ToText(local));
        Assert.Equal("2026-11-26T21:30:05.1200000Z", UtcInstantConverter.ToText(utc));
        Assert.Equal(UtcInstantConverter.ToText(utc).Length, UtcInstantConverter.ToText(DateTimeOffset.MinValue).Length);
    }

    [Fact]
    public void Text_order_is_time_order()
    {
        var instants = new[]
        {
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1),
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, 100, TimeSpan.Zero),
        };

        var byText = instants.OrderBy(UtcInstantConverter.ToText, StringComparer.Ordinal);

        Assert.Equal(instants.Order(), byText);
    }

    [Fact]
    public void An_instant_survives_a_round_trip_as_the_same_instant_in_utc()
    {
        var instant = new DateTimeOffset(2026, 11, 26, 21, 30, 5, TimeSpan.FromHours(-5)).AddTicks(1234567);

        var back = UtcInstantConverter.FromText(UtcInstantConverter.ToText(instant));

        Assert.Equal(instant, back);
        Assert.Equal(TimeSpan.Zero, back.Offset);
    }

    [Fact]
    public void An_entity_id_is_a_lowercase_uuid_version_7_text_and_survives_a_round_trip()
    {
        var id = EntityId.New(new FixedTimeProvider(new DateTimeOffset(2026, 11, 26, 21, 30, 5, TimeSpan.Zero)));

        var text = EntityIdConverter.ToText(id);

        Assert.Equal(7, id.Version);
        Assert.Equal(36, text.Length);
        Assert.Equal(text.ToLowerInvariant(), text);
        Assert.Equal('7', text[14]);
        Assert.Equal(id, Guid.ParseExact(text, "D"));
    }

    [Fact]
    public void An_id_of_another_version_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => EntityIdConverter.ToText(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => EntityIdConverter.ToText(Guid.Empty));
    }
}
