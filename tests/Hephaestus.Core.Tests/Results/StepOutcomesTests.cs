using Hephaestus.Core.Results;

namespace Hephaestus.Core.Tests.Results;

/// <summary>Story 3.14, AD-13: the result of an Implementation is read by strict deserialization, and one that is missing or not whole is never interpreted.</summary>
public class StepOutcomesTests
{
    [Fact]
    public void A_whole_result_is_read()
    {
        var result = StepOutcomes.Read("""{"outcome":"completed","summary":"Done.","questions":[]}""");

        Assert.Equal((StepOutcome.Completed, "Done."), (result!.Outcome, result.Summary));
    }

    [Fact]
    public void A_result_that_needs_a_decision_is_read_with_its_questions()
    {
        var result = StepOutcomes.Read("""{"outcome":"needs-decision","summary":"?","questions":[{"question":"Which?","options":["A","B"]}]}""");

        Assert.Equal(StepOutcome.NeedsDecision, result!.Outcome);
        Assert.Equal(["A", "B"], Assert.Single(result.Questions).Options);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"outcome":"completed"}""")]
    [InlineData("""{"outcome":"completed","summary":"x","questions":null}""")]
    [InlineData("""{"outcome":"completed","summary":"x","questions":[],"extra":true}""")]
    [InlineData("""{"outcome":"done","summary":"x","questions":[]}""")]
    [InlineData("""{"outcome":"needs-decision","summary":"x","questions":[]}""")]
    [InlineData("""{"outcome":"needs-decision","summary":"x","questions":[{"question":" ","options":[]}]}""")]
    [InlineData("""{"outcome":"needs-decision","summary":"x","questions":[{"question":"Q","options":null}]}""")]
    public void A_result_that_is_missing_or_not_whole_is_not_read(string? document)
    {
        Assert.Null(StepOutcomes.Read(document));
    }

    [Fact]
    public void One_question_is_the_decision_and_its_options_are_the_options()
    {
        var request = StepOutcomes.Request(new ImplementationResult(StepOutcome.NeedsDecision, "x", [new DecisionQuestion("Which database?", ["Postgres", "SQLite"])]));

        Assert.Equal("Which database?", request.Decision);
        Assert.Equal(["Postgres", "SQLite"], request.Options);
    }

    [Fact]
    public void Several_questions_are_one_numbered_decision_with_no_options_because_they_are_answered_together()
    {
        var request = StepOutcomes.Request(new ImplementationResult(StepOutcome.NeedsDecision, "x", [new DecisionQuestion("Which database?", ["A"]), new DecisionQuestion("Which cache?", [])]));

        Assert.Equal("1. Which database? 2. Which cache?", request.Decision);
        Assert.Empty(request.Options);
    }
}
