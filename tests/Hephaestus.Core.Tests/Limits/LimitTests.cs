using Hephaestus.Core.Limits;

namespace Hephaestus.Core.Tests.Limits;

/// <summary>Story 1.9, NFR-5: the installation limits have one written form and a range, and a change names only what it changes.</summary>
public class LimitTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("1m", 60)]
    [InlineData("90m", 5400)]
    [InlineData("2h", 7200)]
    [InlineData("2h30m", 9000)]
    [InlineData("1d", 86400)]
    [InlineData("1d2h3m4s", 93784)]
    [InlineData("7d", 604800)]
    [InlineData("60s", 60)]
    public void A_duration_is_days_hours_minutes_and_seconds(string text, int seconds)
    {
        Assert.True(LimitFormat.TryParseDuration(text, out var duration));
        Assert.Equal(TimeSpan.FromSeconds(seconds), duration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0m")]
    [InlineData("59s")]
    [InlineData("7d1s")]
    [InlineData("8d")]
    [InlineData("2h1d")]
    [InlineData("90")]
    [InlineData("1.5h")]
    [InlineData("-1h")]
    [InlineData("2h\n")]
    [InlineData("one hour")]
    public void A_text_that_is_not_a_duration_between_a_minute_and_a_week_is_refused(string? text)
    {
        Assert.False(LimitFormat.TryParseDuration(text, out _));
    }

    [Theory]
    [InlineData(5400, "1h30m")]
    [InlineData(129600, "36h")]
    [InlineData(60, "1m")]
    [InlineData(61, "1m1s")]
    [InlineData(0, "0s")]
    public void A_duration_is_written_in_hours_minutes_and_seconds(int seconds, string expected)
    {
        Assert.Equal(expected, LimitFormat.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData("0.1", 100)]
    [InlineData("0.5", 500)]
    [InlineData("1", 1000)]
    [InlineData("2.5", 2500)]
    [InlineData("1.125", 1125)]
    [InlineData("1024", 1_024_000)]
    public void Processors_are_a_decimal_from_a_tenth_to_1024(string text, int millis)
    {
        Assert.True(LimitFormat.TryParseCpus(text, out var parsed));
        Assert.Equal(millis, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.05")]
    [InlineData("1025")]
    [InlineData("1.2345")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData("two")]
    [InlineData("2\n")]
    public void A_text_that_is_not_a_number_of_processors_in_range_is_refused(string? text)
    {
        Assert.False(LimitFormat.TryParseCpus(text, out _));
    }

    [Theory]
    [InlineData(100, "0.1")]
    [InlineData(1000, "1")]
    [InlineData(2500, "2.5")]
    [InlineData(1125, "1.125")]
    public void Processors_are_written_without_trailing_zeros(int millis, string expected)
    {
        Assert.Equal(expected, LimitFormat.FormatCpus(millis));
    }

    [Theory]
    [InlineData("64m", 64L * 1024 * 1024)]
    [InlineData("512M", 512L * 1024 * 1024)]
    [InlineData("4g", 4L * 1024 * 1024 * 1024)]
    [InlineData("65536k", 64L * 1024 * 1024)]
    [InlineData("4096g", 4096L * 1024 * 1024 * 1024)]
    public void Memory_is_whole_kibibytes_mebibytes_or_gibibytes(string text, long bytes)
    {
        Assert.True(LimitFormat.TryParseMemory(text, out var parsed));
        Assert.Equal(bytes, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("63m")]
    [InlineData("4097g")]
    [InlineData("4")]
    [InlineData("4gb")]
    [InlineData("1.5g")]
    [InlineData("-4g")]
    [InlineData("99999999999g")]
    [InlineData("9999999999g")]
    [InlineData("8589934592g")]
    [InlineData("4294967297g")]
    [InlineData("4t")]
    [InlineData("4g\n")]
    public void A_text_that_is_not_an_amount_of_memory_in_range_is_refused(string? text)
    {
        Assert.False(LimitFormat.TryParseMemory(text, out _));
    }

    [Theory]
    [InlineData(4L * 1024 * 1024 * 1024, "4g")]
    [InlineData(512L * 1024 * 1024, "512m")]
    [InlineData(1536L * 1024 * 1024, "1536m")]
    [InlineData(100L * 1024, "100k")]
    public void Memory_is_written_in_the_largest_unit_that_holds_it_exactly(long bytes, string expected)
    {
        Assert.Equal(expected, LimitFormat.FormatMemory(bytes));
    }

    [Fact]
    public async Task Setting_limits_stores_what_was_named_and_keeps_the_rest()
    {
        var store = new FakeInstallation();
        var service = new LimitService(store);

        var first = await service.SetAsync("2h", "2", "4g", Token);
        var second = await service.SetAsync(null, null, "8g", Token);

        Assert.Equal(LimitFailure.None, first.Failure);
        Assert.Equal(new InstallationLimits(TimeSpan.FromHours(2), 2000, 4L << 30), first.Limits);
        Assert.Equal(new InstallationLimits(TimeSpan.FromHours(2), 2000, 8L << 30), second.Limits);
        Assert.Equal(second.Limits, store.Limits);
    }

    [Fact]
    public async Task Naming_no_limit_is_refused()
    {
        var service = new LimitService(new FakeInstallation());

        Assert.Equal(LimitFailure.NothingGiven, (await service.SetAsync(null, null, null, Token)).Failure);
    }

    [Theory]
    [InlineData("soon", "2", "4g", LimitFailure.InvalidTaskDuration)]
    [InlineData("2h", "lots", "4g", LimitFailure.InvalidCpus)]
    [InlineData("2h", "2", "plenty", LimitFailure.InvalidMemory)]
    [InlineData("soon", "lots", "plenty", LimitFailure.InvalidTaskDuration)]
    public async Task One_value_out_of_shape_refuses_the_whole_change_and_stores_nothing(string duration, string cpus, string memory, LimitFailure expected)
    {
        var store = new FakeInstallation();
        var service = new LimitService(store);

        var result = await service.SetAsync(duration, cpus, memory, Token);

        Assert.Equal(expected, result.Failure);
        Assert.Null(result.Limits);
        Assert.Equal(InstallationLimits.None, store.Limits);
    }

    [Fact]
    public void Digits_of_other_scripts_are_not_digits_of_a_limit_and_are_refused_not_crashed_on()
    {
        var three = ((char)0x0663).ToString();

        Assert.False(LimitFormat.TryParseDuration(three + "m", out _));
        Assert.False(LimitFormat.TryParseCpus(three, out _));
        Assert.False(LimitFormat.TryParseCpus("1." + three, out _));
        Assert.False(LimitFormat.TryParseMemory(three + "g", out _));
    }
}
