using System.Globalization;
using System.Text.RegularExpressions;

namespace Hephaestus.Core.Limits;

/// <summary>
/// The installation's ceilings for one Task's container and run (NFR-5): the most that Project Configuration may ask for, so no
/// repository can raise them (AD-16). A limit nobody set is null, never zero.
/// </summary>
public sealed record InstallationLimits(TimeSpan? TaskDuration, int? CpuMillis, long? MemoryBytes)
{
    public static InstallationLimits None { get; } = new(null, null, null);
}

/// <summary>The limits to change: a null leaves that limit as it is.</summary>
public sealed record LimitsChange(TimeSpan? TaskDuration, int? CpuMillis, long? MemoryBytes);

/// <summary>
/// The one written form of each limit, for the Owner's commands, the documents, and the Project Configuration checked against them.
/// A duration is <c>1h30m</c>, processors a decimal, memory <c>512m</c> or <c>4g</c> (binary units).
/// </summary>
public static partial class LimitFormat
{
    public static readonly TimeSpan MinimumTaskDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumTaskDuration = TimeSpan.FromDays(7);
    public const int MinimumCpuMillis = 100;
    public const int MaximumCpuMillis = 1_024_000;
    public const long MinimumMemoryBytes = 64L * Mebibyte;
    public const long MaximumMemoryBytes = 4096L * Gibibyte;

    private const long Kibibyte = 1024;
    private const long Mebibyte = 1024 * Kibibyte;
    private const long Gibibyte = 1024 * Mebibyte;

    /// <summary>Days, hours, minutes, and seconds, each at most once and in that order (<c>1d2h30m</c>); between one minute and seven days.</summary>
    public static bool TryParseDuration(string? text, out TimeSpan duration)
    {
        duration = default;
        var match = text is null ? Match.Empty : DurationShape().Match(text);
        if (!match.Success || match.Length == 0)
        {
            return false;
        }

        var seconds = 0L;
        var units = new[] { 86_400L, 3_600L, 60L, 1L };
        for (var group = 0; group < units.Length; group++)
        {
            var captured = match.Groups[group + 1];
            if (captured.Success)
            {
                seconds += long.Parse(captured.Value, CultureInfo.InvariantCulture) * units[group];
            }
        }

        duration = TimeSpan.FromSeconds(seconds);
        return duration >= MinimumTaskDuration && duration <= MaximumTaskDuration;
    }

    /// <summary>The written form of a duration of whole seconds: hours, minutes, and seconds (<c>36h</c>, <c>1h30m</c>).</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        var total = (long)duration.TotalSeconds;
        var text = string.Create(CultureInfo.InvariantCulture, $"{(total / 3600 > 0 ? $"{total / 3600}h" : string.Empty)}{(total % 3600 / 60 > 0 ? $"{total % 3600 / 60}m" : string.Empty)}{(total % 60 > 0 ? $"{total % 60}s" : string.Empty)}");
        return text.Length == 0 ? "0s" : text;
    }

    /// <summary>A decimal with at most three places, from 0.1 to 1024 processors.</summary>
    public static bool TryParseCpus(string? text, out int millis)
    {
        millis = 0;
        if (text is null || !CpuShape().IsMatch(text))
        {
            return false;
        }

        var value = decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) * 1000m;
        millis = (int)value;
        return millis is >= MinimumCpuMillis and <= MaximumCpuMillis;
    }

    public static string FormatCpus(int millis) => (millis / 1000m).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Whole kibibytes, mebibytes, or gibibytes (<c>k</c>, <c>m</c>, <c>g</c>), from 64 MiB to 4096 GiB.</summary>
    public static bool TryParseMemory(string? text, out long bytes)
    {
        bytes = 0;
        var match = text is null ? Match.Empty : MemoryShape().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var unit = char.ToLowerInvariant(match.Groups[2].Value[0]) switch
        {
            'k' => Kibibyte,
            'm' => Mebibyte,
            _ => Gibibyte,
        };
        var count = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (count > MaximumMemoryBytes / unit)
        {
            return false;
        }

        bytes = count * unit;
        return bytes is >= MinimumMemoryBytes and <= MaximumMemoryBytes;
    }

    /// <summary>The largest unit that holds the memory exactly.</summary>
    public static string FormatMemory(long bytes) => bytes switch
    {
        _ when bytes % Gibibyte == 0 => string.Create(CultureInfo.InvariantCulture, $"{bytes / Gibibyte}g"),
        _ when bytes % Mebibyte == 0 => string.Create(CultureInfo.InvariantCulture, $"{bytes / Mebibyte}m"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / Kibibyte}k"),
    };

    [GeneratedRegex(@"^(?:([0-9]{1,4})d)?(?:([0-9]{1,5})h)?(?:([0-9]{1,6})m)?(?:([0-9]{1,7})s)?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DurationShape();

    [GeneratedRegex(@"^[0-9]{1,4}(?:\.[0-9]{1,3})?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CpuShape();

    [GeneratedRegex(@"^([0-9]{1,10})([kKmMgG])\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MemoryShape();
}
