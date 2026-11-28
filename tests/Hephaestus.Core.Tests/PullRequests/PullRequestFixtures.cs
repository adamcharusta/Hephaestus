using Hephaestus.Core.Aliases;
using Hephaestus.Core.PullRequests;
using Hephaestus.Core.Results;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.PullRequests;

/// <summary>The facts of a pull request as the tests of Story 3.13 use them.</summary>
internal static class PullRequestFixtures
{
    public static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");

    public static readonly string Tip = new('a', 40);

    public static PullRequestAgent Implementer { get; } = PullRequestAgent.For(Agent.ClaudeCode, "claude-opus-5-5", Effort.High);

    /// <summary>A Validation run that is decided: every command passed.</summary>
    public static ValidationRun Passed()
    {
        var run = ValidationRun.Begin(["dotnet build", "dotnet test"]);
        run = run.Record(ValidationCommandResult.Ended(0, "dotnet build", Guid.CreateVersion7(), 0, StepExitCause.Exited, TimeSpan.FromSeconds(12.34)));
        return run.Record(ValidationCommandResult.Ended(1, "dotnet test", Guid.CreateVersion7(), 0, StepExitCause.Exited, TimeSpan.FromSeconds(60)));
    }

    public static PullRequestFacts Facts(string title = "Add a login page", string summary = "Adds a login page.", ValidationRun? validation = null) =>
        PullRequestFacts.From(title, new ImplementationResult(StepOutcome.Completed, summary, []), Implementer, validation ?? Passed());

    public static PullRequestIntent Intent { get; } = new(StepRunId, Tip);
}
