using System.Text.RegularExpressions;

namespace Hephaestus.Core.Effects;

/// <summary>
/// Neutralises text an Agent wrote before Hephaestus posts it in a comment (AD-19): a Task branch is untrusted code, and what an Agent says is
/// not a way to act on GitHub in Hephaestus's name. HTML comments are escaped, so that the text cannot forge the hidden marker or hide content;
/// mentions are broken, so that nobody is notified by it; issue-closing keywords are broken, so that it closes no issue; and a command keyword
/// is fenced, so that it is shown as code and not read as a command. What stays is readable text; the gateway then masks secret values.
/// </summary>
public static partial class AgentText
{
    /// <summary>A zero-width space, which breaks a mention or a keyword for GitHub and not for the eye.</summary>
    private const char Break = '\u200B';

    /// <summary>The text with the four kinds of effect taken out.</summary>
    public static string Neutralise(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var escaped = CommentEnd().Replace(CommentStart().Replace(text, "&lt;"), "&gt;");
        var mentions = Mention().Replace(escaped, "@" + Break);
        var keywords = ClosingKeyword().Replace(mentions, match => match.Value[0] + Break.ToString() + match.Value[1..]);
        return Command().Replace(keywords, match => "`" + match.Value.Replace('`', '\'') + "`");
    }

    /// <summary>The start of an HTML comment, a declaration, or a processing instruction.</summary>
    [GeneratedRegex(@"<(?=[!?])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CommentStart();

    /// <summary>The <c>&gt;</c> that ends an HTML comment, written <c>--&gt;</c> or <c>--!&gt;</c>.</summary>
    [GeneratedRegex(@"(?<=--!?)>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CommentEnd();

    /// <summary>An <c>@</c> that starts a user's or a team's name.</summary>
    [GeneratedRegex(@"@(?=[A-Za-z0-9_])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Mention();

    /// <summary>A keyword that closes an issue when a reference follows it: <c>#12</c>, <c>owner/repo#12</c>, or an address of an issue or pull request.</summary>
    [GeneratedRegex(
        @"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\b(?=:?\s*(?:(?:[\w.-]+/[\w.-]+)?#\d|https?://))",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ClosingKeyword();

    /// <summary>A <c>/hephaestus</c> command, from the keyword to the end of its line.</summary>
    [GeneratedRegex(@"(?<![\w/.`-])/hephaestus\b[^\r\n]*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Command();
}
