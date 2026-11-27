using Hephaestus.Core.Escalations;

namespace Hephaestus.Core.Tests.Escalations;

/// <summary>FR-48, UX-DR-18: one Escalation asks for one decision, in one line, with options of one line each.</summary>
public class EscalationRequestTests
{
    [Fact]
    public void The_decision_and_each_option_become_one_line()
    {
        var request = EscalationRequest.For("  Which\r\n   database?\n", ["Use  SQLite,\nit is there", " Use Postgres "]);

        Assert.Equal("Which database?", request.Decision);
        Assert.Equal(["Use SQLite, it is there", "Use Postgres"], request.Options);
    }

    [Fact]
    public void A_decision_may_come_with_no_options()
    {
        Assert.Empty(EscalationRequest.For("Is this wanted?", []).Options);
    }

    [Fact]
    public void Text_over_the_bound_is_cut_with_an_ellipsis_and_not_refused()
    {
        var request = EscalationRequest.For(new string('d', 900), [new string('o', 400)]);

        Assert.Equal(EscalationRequest.MaximumDecisionLength, request.Decision.Length);
        Assert.EndsWith("…", request.Decision, StringComparison.Ordinal);
        Assert.Equal(EscalationRequest.MaximumOptionLength, request.Options[0].Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public void A_decision_that_says_nothing_is_refused(string decision) => Assert.Throws<ArgumentException>(() => EscalationRequest.For(decision, []));

    [Fact]
    public void An_empty_option_is_refused() => Assert.Throws<ArgumentException>(() => EscalationRequest.For("Which?", ["A thing", " "]));

    [Fact]
    public void Options_are_lettered_A_to_Z_and_no_more()
    {
        Assert.Equal(26, EscalationRequest.For("Which?", Enumerable.Range(0, 26).Select(i => $"option {i}")).Options.Count);
        Assert.Throws<ArgumentException>(() => EscalationRequest.For("Which?", Enumerable.Range(0, 27).Select(i => $"option {i}")));
    }
}
