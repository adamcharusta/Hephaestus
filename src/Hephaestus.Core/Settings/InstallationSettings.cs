using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Settings;

/// <summary>One installation setting: its name (never translated), its default, the values it accepts as the Owner is told them, and the check of a value. Each story that adds a setting adds it here.</summary>
public sealed record SettingDefinition(string Name, string Default, string Accepts, Func<string, string?> Normalize)
{
    /// <summary>The value as stored for the Owner's text, or null when the text is not a value of this setting.</summary>
    public string? Accept(string text) => Normalize(text);
}

/// <summary>The settings with a default (FR-69 and, later, polling, retries, and retention).</summary>
public static class InstallationSettings
{
    public const string LanguageName = "language";

    public static SettingDefinition Language { get; } = new(
        LanguageName,
        Languages.Default.Tag(),
        string.Join(", ", Enum.GetValues<Localization.Language>().Select(Languages.Tag)),
        text => Languages.TryParse(text, out var language) ? language.Tag() : null);

    /// <summary>Every setting, in the order the Owner sees them.</summary>
    public static IReadOnlyList<SettingDefinition> All { get; } = [Language];

    public static SettingDefinition? Find(string? name) => All.FirstOrDefault(setting => setting.Name == name);

    /// <summary>The installation's language when one was stored; null for a new installation or an entry that is no language.</summary>
    public static Localization.Language? StoredLanguage(IReadOnlyDictionary<string, string> stored) =>
        stored.TryGetValue(LanguageName, out var tag) && Languages.TryParse(tag, out var language) ? language : null;
}
