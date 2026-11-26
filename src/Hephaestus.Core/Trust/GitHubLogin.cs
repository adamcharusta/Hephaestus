using System.Text.RegularExpressions;

namespace Hephaestus.Core.Trust;

/// <summary>The shape of a GitHub login, checked before GitHub is asked so that nothing odd is sent in an address.</summary>
public static partial class GitHubLogin
{
    /// <summary>The longest login GitHub allows.</summary>
    public const int MaximumLength = 39;

    /// <summary>The suffix of the login of a GitHub App's bot account.</summary>
    public const string BotSuffix = "[bot]";

    /// <summary>
    /// Ascii letters, digits, and hyphens inside, up to 39 characters; a bot account's login adds <see cref="BotSuffix"/>. It is
    /// accepted here so that the App's own account is refused as such, not as a malformed name.
    /// </summary>
    public static bool IsValid(string? login) =>
        login is not null && login.Length <= MaximumLength + BotSuffix.Length && Shape().IsMatch(login) && NameLength(login) <= MaximumLength;

    /// <summary>The login of the bot account of the App with <paramref name="slug"/>.</summary>
    public static string BotOf(string slug) => slug + BotSuffix;

    public static bool SameLogin(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static int NameLength(string login) => login.EndsWith(BotSuffix, StringComparison.OrdinalIgnoreCase) ? login.Length - BotSuffix.Length : login.Length;

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?i:\[bot\])?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Shape();
}
