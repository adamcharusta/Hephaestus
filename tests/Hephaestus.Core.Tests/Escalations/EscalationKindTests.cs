using Hephaestus.Core.Escalations;

namespace Hephaestus.Core.Tests.Escalations;

/// <summary>The names an Escalation's kind and outcome are stored by; never translated.</summary>
public class EscalationKindTests
{
    [Theory]
    [InlineData(EscalationKind.Decision, "decision")]
    [InlineData(EscalationKind.EffectFailed, "effect-failed")]
    public void A_kind_has_a_name_that_reads_back(EscalationKind kind, string tag)
    {
        Assert.Equal(tag, kind.Tag());
        Assert.True(EscalationKinds.TryParse(tag, out var parsed));
        Assert.Equal(kind, parsed);
    }

    [Theory]
    [InlineData(EscalationOutcome.Cancelled, "cancelled")]
    [InlineData(EscalationOutcome.Closed, "closed")]
    public void An_outcome_has_a_name_that_reads_back(EscalationOutcome outcome, string tag)
    {
        Assert.Equal(tag, outcome.Tag());
        Assert.True(EscalationOutcomes.TryParse(tag, out var parsed));
        Assert.Equal(outcome, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void A_name_that_is_not_known_is_not_read(string? tag)
    {
        Assert.False(EscalationKinds.TryParse(tag, out _));
        Assert.False(EscalationOutcomes.TryParse(tag, out _));
    }

    [Fact]
    public void A_value_outside_the_set_has_no_name()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((EscalationKind)99).Tag());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((EscalationOutcome)99).Tag());
    }
}
