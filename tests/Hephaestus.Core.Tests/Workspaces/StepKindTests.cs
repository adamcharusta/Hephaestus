using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

/// <summary>Story 3.6, AD-6: only the kinds of step that change code are checkpointed; what any other leaves in the tree is discarded.</summary>
public class StepKindTests
{
    [Theory]
    [InlineData(StepKind.Implementation, true)]
    [InlineData(StepKind.Correction, true)]
    [InlineData(StepKind.Rework, true)]
    [InlineData(StepKind.Readiness, false)]
    [InlineData(StepKind.Planning, false)]
    [InlineData(StepKind.Review, false)]
    [InlineData(StepKind.Validation, false)]
    public void Only_implementation_correction_and_rework_are_checkpointed(StepKind kind, bool expected)
    {
        Assert.Equal(expected, kind.IsCheckpointed());
    }

    [Fact]
    public void Every_kind_has_a_lower_case_tag_of_its_own()
    {
        var tags = Enum.GetValues<StepKind>().Select(kind => kind.Tag()).ToList();

        Assert.Equal(tags.Count, tags.Distinct().Count());
        Assert.All(tags, tag => Assert.Matches("^[a-z]+$", tag));
    }

    [Fact]
    public void A_value_that_is_no_kind_has_no_tag()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((StepKind)99).Tag());
    }
}
