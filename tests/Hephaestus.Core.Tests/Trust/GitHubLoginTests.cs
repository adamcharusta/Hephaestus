using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Tests.Trust;

/// <summary>Story 1.8: the shape of a login is checked before GitHub is asked.</summary>
public class GitHubLoginTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("alice")]
    [InlineData("Alice-Smith")]
    [InlineData("a1")]
    [InlineData("12345")]
    [InlineData("a-b-c")]
    [InlineData("hephaestus[bot]")]
    [InlineData("Hephaestus[BOT]")]
    public void A_login_of_letters_digits_and_inner_hyphens_is_valid(string login) => Assert.True(GitHubLogin.IsValid(login));

    [Fact]
    public void A_login_of_the_longest_length_is_valid_and_one_more_is_not()
    {
        Assert.True(GitHubLogin.IsValid(new string('a', GitHubLogin.MaximumLength)));
        Assert.True(GitHubLogin.IsValid(new string('a', GitHubLogin.MaximumLength) + GitHubLogin.BotSuffix));
        Assert.False(GitHubLogin.IsValid(new string('a', GitHubLogin.MaximumLength + 1)));
        Assert.False(GitHubLogin.IsValid(new string('a', GitHubLogin.MaximumLength + 1) + GitHubLogin.BotSuffix));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-alice")]
    [InlineData("alice-")]
    [InlineData("al ice")]
    [InlineData("alice\n")]
    [InlineData("al_ice")]
    [InlineData("alice/../x")]
    [InlineData("alice%2F")]
    [InlineData("alice?x=1")]
    [InlineData("alice[bot")]
    [InlineData("[bot]")]
    [InlineData("alice[bot][bot]")]
    [InlineData("zoë")]
    [InlineData("\u212Aelvin")]
    [InlineData("alice\u0130")]
    [InlineData("alice[\u212A]")]
    public void Anything_else_is_not_a_login(string? login) => Assert.False(GitHubLogin.IsValid(login));

    [Fact]
    public void The_bot_of_an_App_is_its_slug_and_the_suffix_and_logins_are_compared_ignoring_case()
    {
        Assert.Equal("hephaestus[bot]", GitHubLogin.BotOf("hephaestus"));
        Assert.True(GitHubLogin.SameLogin("Alice", "aLICE"));
        Assert.False(GitHubLogin.SameLogin("alice", "alice2"));
    }
}
