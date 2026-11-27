using Hephaestus.Core.Signals;

namespace Hephaestus.Core.Tests.Signals;

public class SignalStatusTests
{
    [Fact]
    public void Every_status_has_the_name_the_API_reports_and_reads_back()
    {
        Assert.Equal(["pending", "applied", "rejected"], Enum.GetValues<SignalStatus>().Select(status => status.Tag()));
        foreach (var status in Enum.GetValues<SignalStatus>())
        {
            Assert.True(SignalStatuses.TryParse(status.Tag(), out var parsed));
            Assert.Equal(status, parsed);
        }
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("done")]
    [InlineData(null)]
    public void A_name_that_is_not_a_status_does_not_parse(string? tag)
    {
        Assert.False(SignalStatuses.TryParse(tag, out _));
    }

    [Fact]
    public void A_value_outside_the_enumeration_has_no_name()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((SignalStatus)99).Tag());
    }
}
