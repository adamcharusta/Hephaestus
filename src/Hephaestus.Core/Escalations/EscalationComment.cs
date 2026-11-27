using System.Text;
using Hephaestus.Core.Effects;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Escalations;

/// <summary>
/// The comment of an Escalation, in the fixed shape of <c>EXPERIENCE.md</c> (UX-DR-18): the headline <b>Hephaestus needs a decision.</b>, the
/// decision in one sentence, the options lettered one per line, and how to answer. The hidden marker is added last by the gateway. One
/// Escalation asks for one decision. The fixed sentences come from the resources, in the language of the Task (AD-21); what an Agent wrote is
/// quoted, not paraphrased, after it is neutralised (AD-19).
/// </summary>
public static class EscalationComment
{
    /// <summary>The text of the comment of <paramref name="escalation"/>, without the hidden marker.</summary>
    public static string Compose(Escalation escalation, Language language)
    {
        var catalog = MessageCatalog.Instance;
        var (decision, options) = escalation.Kind switch
        {
            EscalationKind.EffectFailed => (FailedEffectSentence(escalation.Effect, language), FailedEffectOptions(language)),
            EscalationKind.PushBlocked => (BlockedPushSentence(escalation, language), BlockedPushOptions(escalation, language)),
            _ => (AgentText.Neutralise(escalation.Decision), escalation.Options.Select(AgentText.Neutralise).ToList()),
        };

        var text = new StringBuilder();
        text.Append(catalog.Resolve(MessageIds.CommentEscalationHeadline, language)).Append("\n\n").Append(decision).Append('\n');
        for (var index = 0; index < options.Count; index++)
        {
            text.Append((char)('A' + index)).Append(". ").Append(options[index]).Append('\n');
        }

        var answer = escalation.ResumeCondition == ResumeConditions.Confirm ? MessageIds.CommentEscalationAnswerConfirm : MessageIds.CommentEscalationAnswer;
        return text.Append('\n').Append(catalog.Resolve(answer, language)).ToString();
    }

    /// <summary>
    /// The sentence for work that was not pushed: the check by its tag, which is never translated, what it means, and what it found. What it found is facts from git, paths and commit ids, which the loop recorded; the paths are a repository's,
    /// so they are neutralised like anything else an Agent's change can name (AD-19).
    /// </summary>
    private static string BlockedPushSentence(Escalation escalation, Language language)
    {
        var catalog = MessageCatalog.Instance;
        var found = AgentText.Neutralise(escalation.Decision);
        if (escalation.Check is not { } check || check == PushCheck.RemoteMoved)
        {
            return found.Length == 0
                ? catalog.Resolve(MessageIds.CommentEscalationPushHeldBare, language)
                : catalog.Resolve(new Message(MessageIds.CommentEscalationPushHeld, found), language);
        }

        var sentence = catalog.Resolve(new Message(MessageIds.CommentEscalationPushBlocked, check.Tag(), catalog.Resolve(CheckId(check), language)), language);
        return found.Length == 0 ? sentence : sentence + " " + catalog.Resolve(new Message(MessageIds.CommentEscalationPushFound, found), language);
    }

    private static List<string> BlockedPushOptions(Escalation escalation, Language language) =>
    [
        MessageCatalog.Instance.Resolve(escalation.ResumeCondition == ResumeConditions.Confirm ? MessageIds.CommentEscalationOptionConfirm : MessageIds.CommentEscalationOptionPushFix, language),
        MessageCatalog.Instance.Resolve(new Message(MessageIds.CommentEscalationOptionCancel, Labels.Marker), language),
    ];

    private static string CheckId(PushCheck check) => check switch
    {
        PushCheck.Import => MessageIds.CommentEscalationCheckImport,
        PushCheck.Branch => MessageIds.CommentEscalationCheckBranch,
        PushCheck.FastForward => MessageIds.CommentEscalationCheckFastForward,
        PushCheck.Secret => MessageIds.CommentEscalationCheckSecret,
        _ => MessageIds.CommentEscalationCheckProtectedPath,
    };

    private static string FailedEffectSentence(FailedEffect? effect, Language language)
    {
        var catalog = MessageCatalog.Instance;
        var reason = catalog.Resolve(ReasonId(effect?.Reason), language);
        return effect switch
        {
            { Kind: EffectKind.Label } => catalog.Resolve(new Message(MessageIds.CommentEscalationEffectLabel, Labels.StatePrefix + effect.Detail, reason), language),
            { Kind: EffectKind.Push } => catalog.Resolve(new Message(MessageIds.CommentEscalationEffectPush, reason), language),
            _ => catalog.Resolve(new Message(MessageIds.CommentEscalationEffectOther, reason), language),
        };
    }

    private static List<string> FailedEffectOptions(Language language) =>
    [
        MessageCatalog.Instance.Resolve(MessageIds.CommentEscalationOptionFix, language),
        MessageCatalog.Instance.Resolve(new Message(MessageIds.CommentEscalationOptionCancel, Labels.Marker), language),
    ];

    private static string ReasonId(string? reason) => reason switch
    {
        EffectReasons.Unreachable => MessageIds.CommentEscalationReasonUnreachable,
        EffectReasons.RateLimited => MessageIds.CommentEscalationReasonRateLimited,
        EffectReasons.NotConnected => MessageIds.CommentEscalationReasonNotConnected,
        EffectReasons.KeyUnreadable => MessageIds.CommentEscalationReasonKeyUnreadable,
        EffectReasons.IssueNotFound => MessageIds.CommentEscalationReasonIssueNotFound,
        EffectReasons.AppNotInstalled => MessageIds.CommentEscalationReasonAppNotInstalled,
        _ => MessageIds.CommentEscalationReasonRejected,
    };
}
