using Hephaestus.Core.Signals;

namespace Hephaestus.Core.Tests.Signals;

/// <summary>AD-2: a Signal's kind is one of the actor-attributed events, stored and shown by a name that is never translated.</summary>
public class SignalKindTests
{
    [Theory]
    [InlineData(SignalKind.Labeled, "labeled")]
    [InlineData(SignalKind.Unlabeled, "unlabeled")]
    [InlineData(SignalKind.Closed, "closed")]
    [InlineData(SignalKind.Reopened, "reopened")]
    [InlineData(SignalKind.CommentCreated, "comment-created")]
    [InlineData(SignalKind.CommentEdited, "comment-edited")]
    [InlineData(SignalKind.StepExited, "step-exited")]
    public void Each_kind_has_one_name_and_the_name_gives_the_kind_back(SignalKind kind, string tag)
    {
        Assert.Equal(tag, kind.Tag());
        Assert.True(SignalKinds.TryParse(tag, out var parsed));
        Assert.Equal(kind, parsed);
    }

    [Fact]
    public void Every_kind_is_covered_so_a_new_one_cannot_be_stored_without_a_name()
    {
        Assert.All(Enum.GetValues<SignalKind>(), kind => Assert.True(SignalKinds.TryParse(kind.Tag(), out _)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Labeled")]
    [InlineData("merged")]
    public void A_name_that_is_not_a_kind_is_refused(string? tag)
    {
        Assert.False(SignalKinds.TryParse(tag, out _));
    }

    [Fact]
    public void A_kind_outside_the_set_has_no_name()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((SignalKind)99).Tag());
    }
}
