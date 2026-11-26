using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Tests.Localization;

public class LanguageResolverTests
{
    [Theory]
    [InlineData("pl", Language.Polish)]
    [InlineData("en", Language.English)]
    [InlineData(null, Language.English)]
    [InlineData("klingon", Language.English)]
    public void The_installation_language_is_its_configured_tag_else_English(string? tag, Language expected)
    {
        Assert.Equal(expected, LanguageResolver.Installation(tag));
    }

    [Theory]
    [InlineData("pl", Language.English, Language.Polish)]
    [InlineData("pl-PL,en;q=0.8", Language.English, Language.Polish)]
    [InlineData("de, pl;q=0.5", Language.English, Language.Polish)]
    [InlineData("en", Language.Polish, Language.English)]
    [InlineData("de, fr", Language.Polish, Language.Polish)]
    [InlineData("", Language.Polish, Language.Polish)]
    [InlineData(null, Language.Polish, Language.Polish)]
    [InlineData(" , ", Language.English, Language.English)]
    public void A_request_names_a_supported_language_else_the_installation_decides(string? header, Language installation, Language expected)
    {
        Assert.Equal(expected, LanguageResolver.ForRequest(header, installation));
    }
}
