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

    /// <summary>
    /// The installation's language: the one the Owner stored, else <paramref name="fallback"/>, which is what the environment names
    /// (English when it names none). The fallback also serves a command that cannot ask the host.
    /// </summary>
    public static Language Installation(Language? stored, Language fallback) => stored ?? fallback;

    /// <summary>
    /// The language of text on GitHub (AD-21): the Task's, else the Project's when no Task has one, else the installation's. The Task's is its
    /// configuration snapshot's and the Project's is its configuration's; neither is stored yet, so callers pass what they have.
    /// </summary>
    public static Language ForText(Language? task, Language? project, Language installation) => task ?? project ?? installation;

    /// <summary>The first supported language an <c>Accept-Language</c> value names, else the installation's.</summary>
    public static Language ForRequest(string? acceptLanguage, Language installation) => FromHeader(acceptLanguage) ?? installation;

    /// <summary>The first supported language an <c>Accept-Language</c> value names; null when it names none.</summary>
    public static Language? FromHeader(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage))
        {
            return null;
        }

        foreach (var entry in acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tag = entry.Split(';', 2)[0].Split('-', 2)[0];
            if (Languages.TryParse(tag, out var language))
            {
                return language;
            }
        }

        return null;
    }
}
