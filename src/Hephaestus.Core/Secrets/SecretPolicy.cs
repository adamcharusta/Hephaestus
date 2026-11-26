using System.Text.RegularExpressions;

namespace Hephaestus.Core.Secrets;

/// <summary>Why a secret value is refused.</summary>
public enum SecretValueRefusal
{
    /// <summary>The value is acceptable.</summary>
    None,

    /// <summary>The value is empty or only white space.</summary>
    Empty,

    /// <summary>The value is shorter than <see cref="SecretPolicy.MinimumValueLength"/>.</summary>
    TooShort,

    /// <summary>The value is longer than <see cref="SecretPolicy.MaximumValueLength"/>.</summary>
    TooLong,

    /// <summary>The value holds U+FFFD, which is what decoding bytes that are not UTF-8 leaves behind.</summary>
    NotText,
}

/// <summary>
/// What may be stored as a secret (AD-16). The name is also the file's name on the secrets volume, so the rule keeps a name
/// from reaching outside that directory. The minimum length keeps the masker (Story 1.6) from masking ordinary text.
/// </summary>
public static partial class SecretPolicy
{
    public const int MinimumValueLength = 8;
    public const int MaximumValueLength = 65_536;
    public const int MaximumNameLength = 64;

    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

    public static SecretValueRefusal CheckValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SecretValueRefusal.Empty;
        }

        if (value.Length < MinimumValueLength)
        {
            return SecretValueRefusal.TooShort;
        }

        if (value.Length > MaximumValueLength)
        {
            return SecretValueRefusal.TooLong;
        }

        // A value is UTF-8 text. Bytes that are not are replaced by U+FFFD on the way in, and storing that would store a credential
        // other than the one the Owner supplied.
        return value.Contains('\uFFFD') ? SecretValueRefusal.NotText : SecretValueRefusal.None;
    }

    /// <summary>
    /// The value a command line sends: one trailing line break is dropped, so that <c>echo value |</c> works, and nothing
    /// else is changed.
    /// </summary>
    public static string TrimTrailingLineBreak(string value)
    {
        if (value.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return value[..^2];
        }

        return value.EndsWith('\n') ? value[..^1] : value;
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9_-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
