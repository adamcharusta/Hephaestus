using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Tests.Escalations;

/// <summary>UX-DR-18, AD-19, AD-21: an Escalation comment has the fixed shape, in the Task's language, and an Agent's text in it is neutralised.</summary>
public class EscalationCommentTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static Escalation Decision(string decision, params string[] options) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), EscalationKind.Decision, decision, options, ResumeConditions.TrustedReply, "s1", Noon);

    private static Escalation Failed(EffectKind kind, string detail, string reason) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), EscalationKind.EffectFailed, string.Empty, [], ResumeConditions.TrustedReply, "g1", Noon, new FailedEffect("k", kind, detail, reason));

    [Fact]
    public void The_comment_has_the_headline_the_decision_the_lettered_options_one_per_line_and_how_to_answer()
    {
        var text = EscalationComment.Compose(Decision("Which database should the login page use?", "SQLite, as the rest does", "Postgres"), Language.English);

        Assert.Equal(
            """
            **Hephaestus needs a decision.**

            Which database should the login page use?
            A. SQLite, as the rest does
            B. Postgres

            Reply on this issue. Any reply from a Trusted User returns the Task to the Queue with the answer.
            """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void A_decision_with_no_options_has_none_to_list()
    {
        var text = EscalationComment.Compose(Decision("Is this wanted?"), Language.English);

        Assert.Equal(
            "**Hephaestus needs a decision.**\n\nIs this wanted?\n\nReply on this issue. Any reply from a Trusted User returns the Task to the Queue with the answer.",
            text);
    }

    [Fact]
    public void Text_an_Agent_wrote_is_neutralised_in_the_decision_and_in_every_option()
    {
        var text = EscalationComment.Compose(
            Decision("<!-- note --> @alice, may it close #4?", "Fixes #9", "/hephaestus confirm"),
            Language.English);

        Assert.DoesNotContain("<!--", text, StringComparison.Ordinal);
        Assert.Contains("&lt;!-- note --&gt; @\u200Balice, may it c\u200Blose #4?", text, StringComparison.Ordinal);
        Assert.Contains("A. F\u200Bixes #9", text, StringComparison.Ordinal);
        Assert.Contains("B. `/hephaestus confirm`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fixed_sentences_are_in_the_language_of_the_Task()
    {
        var text = EscalationComment.Compose(Decision("Którą bazę wybrać?", "SQLite"), Language.Polish);

        Assert.StartsWith("**Hephaestus potrzebuje decyzji.**\n\nKtórą bazę wybrać?\nA. SQLite\n\nOdpowiedz w tym zgłoszeniu.", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EffectReasons.Unreachable, "GitHub stayed unreachable until the limit of attempts.")]
    [InlineData(EffectReasons.RateLimited, "GitHub's rate limit did not lift within the limit of attempts.")]
    [InlineData(EffectReasons.NotConnected, "Hephaestus is not connected to GitHub.")]
    [InlineData(EffectReasons.KeyUnreadable, "The private key of Hephaestus's GitHub App cannot be read.")]
    [InlineData(EffectReasons.IssueNotFound, "GitHub says this issue does not exist.")]
    [InlineData(EffectReasons.AppNotInstalled, "Hephaestus's GitHub App is not installed on this repository.")]
    [InlineData(EffectReasons.Rejected, "GitHub refused the change.")]
    [InlineData("something-new", "GitHub refused the change.")]
    public void An_effect_that_could_not_be_completed_says_which_one_and_why_and_offers_the_two_ways_out(string reason, string why)
    {
        var text = EscalationComment.Compose(Failed(EffectKind.Label, "queued", reason), Language.English);

        Assert.Equal(
            $"""
            **Hephaestus needs a decision.**

            The `hephaestus:queued` label could not be put on this issue, so this Task cannot go on: {why}
            A. Fix the cause, then reply here: the Task returns to the Queue and the change is made again.
            B. Remove the `hephaestus` label to cancel the Task.

            Reply on this issue. Any reply from a Trusted User returns the Task to the Queue with the answer.
            """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void An_effect_that_is_not_a_label_is_described_without_naming_a_label()
    {
        var text = EscalationComment.Compose(Failed(EffectKind.Comment, "x", EffectReasons.Unreachable), Language.English);

        Assert.Contains("A change Hephaestus makes on this issue could not be completed, so this Task cannot go on: GitHub stayed unreachable", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_message_of_the_comment_has_a_Polish_text_that_keeps_the_Glossary_terms()
    {
        var text = EscalationComment.Compose(Failed(EffectKind.Label, "queued", EffectReasons.IssueNotFound), Language.Polish);

        Assert.Contains("`hephaestus:queued`", text, StringComparison.Ordinal);
        Assert.Contains("ten Task nie może iść dalej", text, StringComparison.Ordinal);
        Assert.Contains("Usuń etykietę `hephaestus`, aby anulować Task.", text, StringComparison.Ordinal);
    }
}
