using Hephaestus.Core.Signals;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

/// <summary>Story 3.3, AD-18: the cause of a step's end is one of the runtime's own, stored and shown by a name that is never translated, and the Signal that reports it names the step run.</summary>
public class StepExitCauseTests
{
    [Theory]
    [InlineData(StepExitCause.Exited, "exited")]
    [InlineData(StepExitCause.Timeout, "timeout")]
    [InlineData(StepExitCause.OutOfMemory, "out-of-memory")]
    [InlineData(StepExitCause.Lost, "lost")]
    public void Each_cause_has_one_name_and_the_name_gives_the_cause_back(StepExitCause cause, string tag)
    {
        Assert.Equal(tag, cause.Tag());
        Assert.True(StepExitCauses.TryParse(tag, out var parsed));
        Assert.Equal(cause, parsed);
    }

    [Fact]
    public void Every_cause_is_covered_so_a_new_one_cannot_be_stored_without_a_name()
    {
        Assert.All(Enum.GetValues<StepExitCause>(), cause => Assert.True(StepExitCauses.TryParse(cause.Tag(), out _)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Timeout")]
    [InlineData("killed")]
    public void A_name_that_is_not_a_cause_is_refused(string? tag)
    {
        Assert.False(StepExitCauses.TryParse(tag, out _));
    }

    [Fact]
    public void A_cause_outside_the_set_has_no_name()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((StepExitCause)99).Tag());
    }

    [Fact]
    public void The_Signal_of_a_step_that_ended_is_keyed_by_the_step_run_so_that_a_second_one_is_the_same_event()
    {
        var stepRun = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
        var when = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

        var signal = Signal.StepExited(stepRun, 137, StepExitCause.Timeout, when);

        Assert.Equal((Signal.RuntimeSource, "018f4b6e-0000-7000-8000-000000000002", SignalKind.StepExited), (signal.Source, signal.SourceEventId, signal.Kind));
        Assert.Equal((stepRun, 137, StepExitCause.Timeout, when), (signal.StepRunId, signal.ExitCode, signal.Cause, signal.OccurredAt));
        Assert.Equal((0L, 0, (long?)null, (string?)null), (signal.RepositoryId, signal.IssueNumber, signal.ActorId, signal.ActorLogin));
        Assert.Equal((signal.Source, signal.SourceEventId), (Signal.StepExited(stepRun, 0, StepExitCause.Exited, when.AddHours(1)).Source, Signal.StepExited(stepRun, 0, StepExitCause.Exited, when.AddHours(1)).SourceEventId));
    }
}
