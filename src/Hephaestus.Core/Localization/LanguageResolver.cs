namespace Hephaestus.Core.Localization;

/// <summary>
/// The one function that decides the language of a sentence (AD-21). For the command line and the API's answers to it the
/// language is the installation's; a client that already knows it, such as the command line, may name it in an
/// <c>Accept-Language</c> header so that both ends agree.
/// </summary>
public static class LanguageResolver
{
    /// <summary>The installation's language from its configured tag; an absent or unknown tag is English.</summary>
    public static Language Installation(string? configuredTag) =>
        Languages.TryParse(configuredTag, out var language) ? language : Languages.Default;

    /// <summary>The first supported language an <c>Accept-Language</c> value names, else the installation's.</summary>
    public static Language ForRequest(string? acceptLanguage, Language installation)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage))
        {
            return installation;
        }

        foreach (var entry in acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tag = entry.Split(';', 2)[0].Split('-', 2)[0];
            if (Languages.TryParse(tag, out var language))
            {
                return language;
            }
        }

        return installation;
    }
}
