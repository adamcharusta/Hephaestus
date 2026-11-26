namespace Hephaestus.Core.Localization;

/// <summary>A language Hephaestus writes sentences in (FR-69). English is the default; Polish is selectable.</summary>
public enum Language
{
    English,
    Polish,
}

public static class Languages
{
    public static Language Default => Language.English;

    /// <summary>The two-letter tag a configuration value, an environment variable, or an HTTP header carries.</summary>
    public static string Tag(this Language language) => language switch
    {
        Language.English => "en",
        Language.Polish => "pl",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    public static bool TryParse(string? tag, out Language language)
    {
        switch (tag?.Trim().ToLowerInvariant())
        {
            case "en":
                language = Language.English;
                return true;
            case "pl":
                language = Language.Polish;
                return true;
            default:
                language = Default;
                return false;
        }
    }
}
