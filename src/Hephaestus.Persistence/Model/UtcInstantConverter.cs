using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hephaestus.Persistence.Model;

/// <summary>
/// The one converter for every instant (AR-32): UTC, fixed-width ISO 8601 text with seven fractional digits, so that
/// string order is time order.
/// </summary>
internal sealed class UtcInstantConverter() : ValueConverter<DateTimeOffset, string>(instant => ToText(instant), text => FromText(text))
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public static string ToText(DateTimeOffset instant) => instant.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    public static DateTimeOffset FromText(string text) =>
        DateTimeOffset.ParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
