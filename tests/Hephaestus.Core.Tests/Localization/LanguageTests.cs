using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Tests.Localization;

public class LanguageTests
{
    [Theory]
    [InlineData("en", Language.English)]
    [InlineData("pl", Language.Polish)]
    [InlineData(" PL ", Language.Polish)]
    public void A_supported_tag_is_parsed(string tag, Language expected)
    {
        Assert.True(Languages.TryParse(tag, out var language));
        Assert.Equal(expected, language);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("polish")]
    public void An_absent_or_unsupported_tag_is_not_parsed_and_yields_English(string? tag)
    {
        Assert.False(Languages.TryParse(tag, out var language));
        Assert.Equal(Language.English, language);
    }

    [Fact]
    public void Tags_round_trip_and_an_undefined_language_is_refused()
    {
        Assert.Equal("en", Language.English.Tag());
        Assert.Equal("pl", Language.Polish.Tag());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((Language)42).Tag());
    }
}
