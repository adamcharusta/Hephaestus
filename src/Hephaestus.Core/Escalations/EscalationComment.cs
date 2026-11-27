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
        var (decision, options) = escalation.Kind == EscalationKind.EffectFailed
            ? (FailedEffectSentence(escalation.Effect, language), FailedEffectOptions(language))
            : (AgentText.Neutralise(escalation.Decision), escalation.Options.Select(AgentText.Neutralise).ToList());

        var text = new StringBuilder();
        text.Append(catalog.Resolve(MessageIds.CommentEscalationHeadline, language)).Append("\n\n").Append(decision).Append('\n');
        for (var index = 0; index < options.Count; index++)
        {
            text.Append((char)('A' + index)).Append(". ").Append(options[index]).Append('\n');
        }

        return text.Append('\n').Append(catalog.Resolve(MessageIds.CommentEscalationAnswer, language)).ToString();
    }

    private static string FailedEffectSentence(FailedEffect? effect, Language language)
    {
        var catalog = MessageCatalog.Instance;
        var reason = catalog.Resolve(ReasonId(effect?.Reason), language);
        return effect is { Kind: EffectKind.Label }
            ? catalog.Resolve(new Message(MessageIds.CommentEscalationEffectLabel, Labels.StatePrefix + effect.Detail, reason), language)
            : catalog.Resolve(new Message(MessageIds.CommentEscalationEffectOther, reason), language);
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
