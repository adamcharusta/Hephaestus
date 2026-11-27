using System.Globalization;
using System.Text;

namespace Hephaestus.Core.Projects;

/// <summary>
/// Text taken from a configuration file, made safe to print. The file is repository content that a pull request changed, so a value in it is
/// never written to a terminal as it is: a control character (an escape sequence), a format character (a text direction mark), or a line or
/// paragraph separator would otherwise change what the Owner reads.
/// </summary>
public static class ConfigurationText
{
    /// <summary>The longest a value is echoed in a message.</summary>
    public const int EchoLength = 60;

    /// <summary>The value with each unsafe character replaced by <c>?</c>, and cut to <paramref name="maximumLength"/> characters with an ellipsis when longer.</summary>
    public static string Safe(string? value, int maximumLength = EchoLength)
    {
        value ??= string.Empty;
        var builder = new StringBuilder(Math.Min(value.Length, maximumLength) + 1);
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count == maximumLength)
            {
                builder.Append('…');
                break;
            }

            var category = Rune.GetUnicodeCategory(rune);
            var unsafeCharacter = Rune.IsControl(rune)
                || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;
            builder.Append(unsafeCharacter ? '?' : rune.ToString());
            count++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// A command or other multi-line value on one line of output: a line break, a carriage return, and a tab are written as <c>\n</c>, <c>\r</c>, and <c>\t</c>, so that what
    /// is printed is what would run, and anything else unsafe is replaced as <see cref="Safe"/> does.
    /// </summary>
    public static string Line(string? value, int maximumLength = 2000) =>
        Safe((value ?? string.Empty).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal), maximumLength);
}
