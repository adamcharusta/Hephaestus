using Hephaestus.Core.Localization;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Effects;

/// <summary>
/// The comments Hephaestus posts to say what it ignored (Story 2.5, AD-15). The intent of a comment is journaled with the Signal that causes
/// it, so what it says is kept in the intent's detail as a kind and its facts, and the sentence is resolved from the resources when the
/// comment is made, in the language of the Task, or of the Project, or of the installation (AD-21). A user who is not a Trusted User gets one
/// comment for each kind of action on each issue, because a reply for every attempt would let outsiders fill the thread.
/// </summary>
public static class IssueNotices
{
    /// <summary>A label that Hephaestus reads was applied by a user who is not a Trusted User.</summary>
    public const string IgnoredLabelKind = "ignored-label";

    private const char Separator = '|';

    /// <summary>
    /// The comment that says a label applied by <paramref name="actorLogin"/> was ignored, for the issue and the Task it concerns, if it
    /// has one. Null when the login is not a GitHub login, so that nothing but a login is ever put in a comment as one.
    /// </summary>
    /// <param name="label">The label as Hephaestus names it, see <see cref="GitHub.Labels.ReadLabel"/>.</param>
    public static NewEffect? IgnoredLabel(Guid? taskId, string cause, long repositoryId, int issueNumber, long actorId, string? actorLogin, string label) =>
        actorLogin is not null && GitHubLogin.IsValid(actorLogin)
            ? new NewEffect(
                taskId,
                cause,
                EffectKind.Comment,
                string.Join(Separator, IgnoredLabelKind, actorLogin, label),
                repositoryId,
                issueNumber,
                NoticeKey: $"{IgnoredLabelKind}:{actorId.ToString(System.Globalization.CultureInfo.InvariantCulture)}")
            : null;

    /// <summary>The sentence of a comment whose intent holds <paramref name="detail"/>; false when the detail is of no kind that is known.</summary>
    public static bool TryWrite(string detail, Language language, out string text)
    {
        var parts = detail.Split(Separator);
        if (parts is [IgnoredLabelKind, var login, var label])
        {
            text = MessageCatalog.Instance.Resolve(new Message(MessageIds.CommentIgnoredLabel, login, label), language);
            return true;
        }

        text = string.Empty;
        return false;
    }
}
