using Hephaestus.Core.Effects;
using Hephaestus.Core.Settings;

namespace Hephaestus.Core.Tests.Effects;

/// <summary>Story 2.4, AD-5: the key of an effect, the names the journal stores, and the bounds of a retry.</summary>
public class EffectTests
{
    private static readonly Guid Task = Guid.Parse("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b");

    [Fact]
    public void The_key_is_scope_cause_effect_kind_and_ordinal_and_a_signal_cause_is_g_and_its_sequence()
    {
        Assert.Equal("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g12:label:0", EffectKeys.For(Task, EffectKeys.SignalCause(12), EffectKind.Label, 0));
        Assert.Equal("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g12:label:3", EffectKeys.For(Task, "g12", EffectKind.Label, 3));
    }

    [Fact]
    public void Every_kind_result_and_entry_has_a_name_that_reads_back_and_a_value_outside_the_enumeration_has_none()
    {
        foreach (var kind in Enum.GetValues<EffectKind>())
        {
            Assert.True(EffectKinds.TryParse(kind.Tag(), out var parsed));
            Assert.Equal(kind, parsed);
        }

        foreach (var result in Enum.GetValues<EffectResult>())
        {
            Assert.True(EffectResults.TryParse(result.Tag(), out var parsed));
            Assert.Equal(result, parsed);
        }

        foreach (var entry in Enum.GetValues<JournalEntryKind>())
        {
            Assert.True(JournalEntryKinds.TryParse(entry.Tag(), out var parsed));
            Assert.Equal(entry, parsed);
        }

        Assert.Equal(["applied", "already-in-place", "superseded", "gave-up", "refused"], Enum.GetValues<EffectResult>().Select(EffectResults.Tag));
        Assert.Equal(["intent", "retry", "result"], Enum.GetValues<JournalEntryKind>().Select(JournalEntryKinds.Tag));
        Assert.False(EffectKinds.TryParse("comment", out _));
        Assert.False(EffectResults.TryParse(null, out _));
        Assert.False(JournalEntryKinds.TryParse("", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((EffectKind)9).Tag());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((EffectResult)9).Tag());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((JournalEntryKind)9).Tag());
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    [InlineData(6, 600)]
    [InlineData(40, 600)]
    public void The_wait_after_a_failure_doubles_from_thirty_seconds_and_stops_at_ten_minutes(int failures, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), EffectRetries.DelayAfter(failures));
    }

    [Fact]
    public void A_new_installation_retries_an_effect_five_times()
    {
        Assert.Equal(5, InstallationSettings.EffectRetriesFrom(new Dictionary<string, string>()));
        Assert.Equal("0-20", InstallationSettings.EffectRetries.Accepts);
        Assert.Equal("5", InstallationSettings.EffectRetries.Default);
        Assert.Same(InstallationSettings.EffectRetries, InstallationSettings.Find("effectRetries"));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("7", "7")]
    [InlineData("20", "20")]
    public void The_bound_of_retries_is_stored_as_a_whole_number_from_zero_to_twenty_and_read_back(string value, string stored)
    {
        Assert.Equal(stored, InstallationSettings.EffectRetries.Accept(value));
        Assert.Equal(int.Parse(stored, System.Globalization.CultureInfo.InvariantCulture), InstallationSettings.EffectRetriesFrom(new Dictionary<string, string> { ["effectRetries"] = value }));
    }

    [Theory]
    [InlineData("21")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("5x")]
    [InlineData("")]
    [InlineData("٥")]
    [InlineData("100")]
    public void A_bound_outside_zero_to_twenty_or_not_a_whole_number_is_refused_and_a_stored_one_that_is_no_longer_valid_reads_as_the_default(string value)
    {
        Assert.Null(InstallationSettings.EffectRetries.Accept(value));
        Assert.Equal(5, InstallationSettings.EffectRetriesFrom(new Dictionary<string, string> { ["effectRetries"] = value }));
    }
}
