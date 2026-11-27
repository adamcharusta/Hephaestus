using Hephaestus.Workspaces.Egress;

namespace Hephaestus.Workspaces.Tests.Egress;

/// <summary>Story 3.4, AD-18: what the proxy writes to its log, and what the runtime reads back from it.</summary>
public class EgressRefusalsTests
{
    [Fact]
    public void The_ready_line_is_found_in_a_log_and_only_as_a_whole_line()
    {
        Assert.True(EgressRefusals.IsReady("listening 3128\n"));
        Assert.True(EgressRefusals.IsReady("something\nlistening 3128\nrefused x not-allowed\n"));
        Assert.False(EgressRefusals.IsReady(string.Empty));
        Assert.False(EgressRefusals.IsReady("not listening 3128\n"));
        Assert.Equal("listening 3128", EgressRefusals.ReadyLine(3128));
    }

    [Fact]
    public void Refused_hosts_are_read_each_once_in_the_order_they_were_first_refused()
    {
        var log = string.Join('\n', [
            EgressRefusals.ReadyLine(3128),
            EgressRefusals.RefusalLine("evil.example", EgressRefusals.NotAllowed),
            EgressRefusals.RefusalLine("169.254.169.254", EgressRefusals.PrivateAddress),
            EgressRefusals.RefusalLine("evil.example", EgressRefusals.NotAllowed),
            "error SocketException",
            EgressRefusals.RefusalLine("[::1]", EgressRefusals.PrivateAddress),
            EgressRefusals.RefusalLine("mail.example", EgressRefusals.PortNotAllowed),
            string.Empty,
        ]);

        Assert.Equal(["evil.example", "169.254.169.254", "[::1]", "mail.example"], EgressRefusals.Parse(log));
    }

    [Theory]
    [InlineData("refused evil.example because\n")]
    [InlineData("refused  not-allowed\n")]
    [InlineData("refused evil.example not-allowed extra\n")]
    [InlineData(" refused evil.example not-allowed\n")]
    [InlineData("refused evil example not-allowed\n")]
    public void A_line_that_is_not_a_refusal_as_the_proxy_writes_one_is_not_read_as_one(string line)
    {
        Assert.Empty(EgressRefusals.Parse(line));
    }

    [Fact]
    public void A_log_with_a_carriage_return_is_read_too()
    {
        Assert.Equal(["evil.example"], EgressRefusals.Parse("listening 3128\r\nrefused evil.example not-allowed\r\n"));
    }

    [Fact]
    public void At_most_a_hundred_hosts_are_read_so_a_step_cannot_make_the_Signal_as_large_as_it_likes()
    {
        var log = string.Join('\n', Enumerable.Range(0, 150).Select(i => EgressRefusals.RefusalLine($"host{i}.example", EgressRefusals.NotAllowed)));

        Assert.Equal(100, EgressRefusals.Parse(log).Count);
    }

    [Theory]
    [InlineData("api.nuget.org", "api.nuget.org")]
    [InlineData("[2001:db8::1]", "[2001:db8::1]")]
    [InlineData("under_score.example", "under_score.example")]
    [InlineData("has space.example", null)]
    [InlineData("new\nline", null)]
    [InlineData("", null)]
    public void Only_a_host_made_of_the_characters_of_a_name_or_an_address_is_logged(string host, string? expected)
    {
        Assert.Equal(expected, EgressRefusals.Loggable(host));
    }

    [Fact]
    public void A_host_longer_than_a_name_can_be_is_not_logged()
    {
        Assert.Null(EgressRefusals.Loggable(new string('a', 254)));
        Assert.NotNull(EgressRefusals.Loggable(new string('a', 253)));
    }
}
