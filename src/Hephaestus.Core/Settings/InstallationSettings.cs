using Hephaestus.Core.Localization;

using System.Globalization;

namespace Hephaestus.Core.Settings;

/// <summary>One installation setting: its name (never translated), its default, the values it accepts as the Owner is told them, and the check of a value. Each story that adds a setting adds it here.</summary>
public sealed record SettingDefinition(string Name, string Default, string Accepts, Func<string, string?> Normalize)
{
    /// <summary>The value as stored for the Owner's text, or null when the text is not a value of this setting.</summary>
    public string? Accept(string text) => Normalize(text);
}

/// <summary>The settings with a default (FR-69, the polling interval, and, later, retries and retention).</summary>
public static class InstallationSettings
{
    public const string LanguageName = "language";
    public const string PollingIntervalName = "pollingInterval";
    public const string EffectRetriesName = "effectRetries";

    /// <summary>The polling cycle in seconds (AD-1): how often each registered Project is asked for what happened on GitHub.</summary>
    public const int DefaultPollingIntervalSeconds = 60;
    public const int MinimumPollingIntervalSeconds = 10;
    public const int MaximumPollingIntervalSeconds = 3600;

    /// <summary>How many times an attempt at an effect on GitHub may fail and be tried again before the Task waits for the Owner (AD-5, NFR-12).</summary>
    public const int DefaultEffectRetries = 5;
    public const int MinimumEffectRetries = 0;
    public const int MaximumEffectRetries = 20;

    public static SettingDefinition Language { get; } = new(
        LanguageName,
        Languages.Default.Tag(),
        string.Join(", ", Enum.GetValues<Localization.Language>().Select(Languages.Tag)),
        text => Languages.TryParse(text, out var language) ? language.Tag() : null);

    /// <summary>
    /// The polling cycle, in whole seconds. It is the cycle that FR-11 and FR-29 measure against: a change on GitHub is noticed within
    /// one cycle, and GitHub may ask for a longer wait than this between two requests for the same repository.
    /// </summary>
    public static SettingDefinition PollingInterval { get; } = new(
        PollingIntervalName,
        DefaultPollingIntervalSeconds.ToString(CultureInfo.InvariantCulture),
        $"{MinimumPollingIntervalSeconds}-{MaximumPollingIntervalSeconds} s",
        text => TryParseSeconds(text, out var seconds) ? seconds.ToString(CultureInfo.InvariantCulture) : null);

    /// <summary>
    /// How many times an attempt at an effect on GitHub, such as showing a Task's state on its issue, may fail and be tried again, at the
    /// times recorded in the journal, before the Task moves to <c>waiting-for-owner</c>. 0 gives the Task up to the Owner at the first failure.
    /// </summary>
    public static SettingDefinition EffectRetries { get; } = new(
        EffectRetriesName,
        DefaultEffectRetries.ToString(CultureInfo.InvariantCulture),
        $"{MinimumEffectRetries}-{MaximumEffectRetries}",
        text => TryParseEffectRetries(text, out var retries) ? retries.ToString(CultureInfo.InvariantCulture) : null);

    /// <summary>Every setting, in the order the Owner sees them.</summary>
    public static IReadOnlyList<SettingDefinition> All { get; } = [Language, PollingInterval, EffectRetries];

    public static SettingDefinition? Find(string? name) => All.FirstOrDefault(setting => setting.Name == name);

    /// <summary>The installation's language when one was stored; null for a new installation or an entry that is no language.</summary>
    public static Localization.Language? StoredLanguage(IReadOnlyDictionary<string, string> stored) =>
        stored.TryGetValue(LanguageName, out var tag) && Languages.TryParse(tag, out var language) ? language : null;

    /// <summary>The polling cycle now: the stored value, or the default when none was stored or the stored one is no value of the setting.</summary>
    public static TimeSpan PollingIntervalFrom(IReadOnlyDictionary<string, string> stored) =>
        TimeSpan.FromSeconds(stored.TryGetValue(PollingIntervalName, out var text) && TryParseSeconds(text, out var seconds) ? seconds : DefaultPollingIntervalSeconds);

    /// <summary>The bound of retries of an effect now: the stored value, or the default when none was stored or the stored one is no value of the setting.</summary>
    public static int EffectRetriesFrom(IReadOnlyDictionary<string, string> stored) =>
        stored.TryGetValue(EffectRetriesName, out var text) && TryParseEffectRetries(text, out var retries) ? retries : DefaultEffectRetries;

    private static bool TryParseEffectRetries(string? text, out int retries)
    {
        retries = 0;
        return text is { Length: > 0 and <= 2 }
            && text.All(character => character is >= '0' and <= '9')
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out retries)
            && retries is >= MinimumEffectRetries and <= MaximumEffectRetries;
    }

    // Ascii digits only: `\d`-like parsing would take the digits of other scripts and then fail on them.
    private static bool TryParseSeconds(string? text, out int seconds)
    {
        seconds = 0;
        return text is { Length: > 0 and <= 4 }
            && text.All(character => character is >= '0' and <= '9')
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
            && seconds is >= MinimumPollingIntervalSeconds and <= MaximumPollingIntervalSeconds;
    }
}
