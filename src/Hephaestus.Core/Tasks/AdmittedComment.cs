using System.Security.Cryptography;
using System.Text;

namespace Hephaestus.Core.Tasks;

/// <summary>
/// A comment on a Task's issue that a Trusted User wrote and the loop admitted (AD-15): content an Agent may see as context for later steps.
/// A comment that answers an Escalation says which one. The hash is what a later comparison uses to notice an edit.
/// </summary>
/// <param name="TaskId">The Task whose issue the comment is on.</param>
/// <param name="CommentId">GitHub's id of the comment.</param>
/// <param name="AuthorId">GitHub's numeric id of the author, who was a Trusted User when the comment was admitted.</param>
/// <param name="AuthorLogin">The author's login at the time, for display only.</param>
/// <param name="Body">The text as GitHub gave it.</param>
/// <param name="Hash">The hash of the text, <see cref="CommentContent.Hash"/>.</param>
/// <param name="SignalSequence">The inbox sequence of the Signal that vouched for this content.</param>
/// <param name="AdmittedAt">When the loop admitted it.</param>
/// <param name="AnswersEscalationId">The Escalation the comment answered; null for a comment that is context only.</param>
public sealed record AdmittedComment(
    Guid TaskId,
    long CommentId,
    long AuthorId,
    string? AuthorLogin,
    string Body,
    string Hash,
    long SignalSequence,
    DateTimeOffset AdmittedAt,
    Guid? AnswersEscalationId = null);

/// <summary>What the loop reads of a comment's text.</summary>
public static class CommentContent
{
    /// <summary>The word that begins the first line of a command to Hephaestus (Story 2.11 parses what follows it).</summary>
    public const string CommandPrefix = "/hephaestus";

    /// <summary>The SHA-256 of the text as GitHub gave it, in lower-case hexadecimal, with no normalization.</summary>
    public static string Hash(string body) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    /// <summary>
    /// Whether the first line of the comment begins with <c>/hephaestus</c>, so that it is a command and is neither an answer nor context.
    /// Neither leading white space, an invisible format character (a zero-width space, a byte order mark), nor letter case hides it: a command that
    /// is read as an answer would resume a Task.
    /// </summary>
    public static bool IsCommand(string body)
    {
        var start = 0;
        while (start < body.Length && (char.IsWhiteSpace(body[start]) || char.GetUnicodeCategory(body[start]) == System.Globalization.UnicodeCategory.Format))
        {
            start++;
        }

        return body.AsSpan(start).StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
