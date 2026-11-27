using Hephaestus.Core.Failures;

namespace Hephaestus.Core.Tests.Failures;

/// <summary>AD-11: a step run fails with exactly one of seven classes, stored and shown by a name that is never translated.</summary>
public class FailureClassTests
{
    [Theory]
    [InlineData(FailureClass.UsageLimit, "usage-limit")]
    [InlineData(FailureClass.Timeout, "timeout")]
    [InlineData(FailureClass.AgentError, "agent-error")]
    [InlineData(FailureClass.ValidationFailure, "validation-failure")]
    [InlineData(FailureClass.InfrastructureError, "infrastructure-error")]
    [InlineData(FailureClass.Cancelled, "cancelled")]
    [InlineData(FailureClass.Unknown, "unknown")]
    public void Each_class_has_one_name_and_the_name_gives_the_class_back(FailureClass failure, string tag)
    {
        Assert.Equal(tag, failure.Tag());
        Assert.True(FailureClasses.TryParse(tag, out var parsed));
        Assert.Equal(failure, parsed);
    }

    [Fact]
    public void There_are_seven_classes_and_none_is_unnamed()
    {
        Assert.Equal(7, Enum.GetValues<FailureClass>().Length);
        Assert.All(Enum.GetValues<FailureClass>(), failure => Assert.True(FailureClasses.TryParse(failure.Tag(), out _)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Timeout")]
    [InlineData("rate-limit")]
    public void A_name_that_is_not_a_class_is_refused(string? tag)
    {
        Assert.False(FailureClasses.TryParse(tag, out _));
    }

    [Fact]
    public void A_class_outside_the_set_has_no_name()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((FailureClass)99).Tag());
    }
}
