using Hephaestus.Core.Invocations;

namespace Hephaestus.Core.Tests.Invocations;

/// <summary>AD-10, FR-25: an authentication mode is stored and shown by a name that is never translated.</summary>
public class AuthenticationModeTests
{
    [Theory]
    [InlineData(AuthenticationMode.ApiKey, "api-key")]
    [InlineData(AuthenticationMode.Subscription, "subscription")]
    public void Each_mode_has_one_name_and_the_name_gives_the_mode_back(AuthenticationMode mode, string tag)
    {
        Assert.Equal(tag, mode.Tag());
        Assert.True(AuthenticationModes.TryParse($" {tag.ToUpperInvariant()} ", out var parsed));
        Assert.Equal(mode, parsed);
        Assert.Contains(tag, AuthenticationModes.Tags);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("oauth")]
    public void A_name_that_is_not_a_mode_is_refused(string? tag)
    {
        Assert.False(AuthenticationModes.TryParse(tag, out _));
    }

    [Fact]
    public void A_mode_outside_the_set_has_no_name()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((AuthenticationMode)99).Tag());
    }
}
