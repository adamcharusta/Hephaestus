using Hephaestus.Core.Aliases;
using Hephaestus.Core.PullRequests;
using Hephaestus.Core.Results;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;
using static Hephaestus.Core.Tests.PullRequests.PullRequestFixtures;

namespace Hephaestus.Core.Tests.PullRequests;

/// <summary>Story 3.13, FR-30, FR-31: the facts are made from the records, keep what the runtime saw of each Validation command, and are read back only when they are ours.</summary>
public class PullRequestFactsTests
{
    [Fact]
    public void The_facts_name_who_implemented_and_each_command_with_what_the_runtime_saw_and_nothing_an_Agent_said_of_them()
    {
        var run = ValidationRun.Begin(["dotnet build", "dotnet test", "dotnet format"])
            .Record(ValidationCommandResult.Ended(0, "dotnet build", Guid.CreateVersion7(), 0, StepExitCause.Exited, TimeSpan.FromMilliseconds(1500)))
            .Record(ValidationCommandResult.Ended(1, "dotnet test", Guid.CreateVersion7(), 1, StepExitCause.Exited, TimeSpan.FromSeconds(3)));

        var facts = Facts(validation: run);

        Assert.Equal(new PullRequestAgent("claude-code", "claude-opus-5-5", "high"), facts.Implementer);
        Assert.Equal(
            [
                new PullRequestCheck("dotnet build", "passed", 0, 1500),
                new PullRequestCheck("dotnet test", "failed", 1, 3000),
                new PullRequestCheck("dotnet format", "not-run", null, null),
            ],
            facts.Checks);
    }

    [Fact]
    public void Facts_made_before_Validation_is_decided_are_a_mistake_and_so_is_a_missing_part()
    {
        var undecided = ValidationRun.Begin(["dotnet build"]);

        Assert.Throws<InvalidOperationException>(() => Facts(validation: undecided));
        Assert.Throws<ArgumentNullException>(() => PullRequestFacts.From("t", null!, Implementer, Passed()));
        Assert.Throws<ArgumentNullException>(() => PullRequestFacts.From("t", new ImplementationResult(StepOutcome.Completed, "s", []), null!, Passed()));
        Assert.Throws<ArgumentNullException>(() => PullRequestFacts.From("t", new ImplementationResult(StepOutcome.Completed, "s", []), Implementer, null!));
        Assert.Throws<ArgumentNullException>(() => PullRequestCheck.From(null!));
    }

    [Fact]
    public void A_summary_that_is_longer_than_the_bound_is_cut_there()
    {
        var facts = Facts(summary: new string('x', PullRequestFacts.MaximumSummaryLength + 50));

        Assert.Equal(PullRequestFacts.MaximumSummaryLength, facts.Summary.Length);
    }

    [Fact]
    public void The_facts_read_back_as_they_were_written()
    {
        var facts = Facts();

        Assert.True(PullRequestFacts.TryParse(facts.ToJson(), out var read));

        Assert.Equal(facts.Title, read.Title);
        Assert.Equal(facts.Summary, read.Summary);
        Assert.Equal(facts.Implementer, read.Implementer);
        Assert.Equal(facts.Checks, read.Checks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"title\":\"t\",\"summary\":\"s\",\"implementer\":{\"agent\":\"a\",\"model\":\"m\",\"effort\":\"e\"},\"checks\":[],\"extra\":1}")]
    public void A_payload_that_is_not_ours_is_not_read(string? payload)
    {
        Assert.False(PullRequestFacts.TryParse(payload, out _));
    }

    [Fact]
    public void The_agent_is_named_by_its_tags()
    {
        Assert.Equal(new PullRequestAgent("codex-cli", "gpt-x", "xhigh"), PullRequestAgent.For(Agent.CodexCli, "gpt-x", Effort.ExtraHigh));
    }
}
