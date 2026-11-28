using Hephaestus.Core.Runs;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Runs;

/// <summary>Story 3.14, AD-1, AD-18: a step run only goes forward, its statuses have names that are stored and never translated, and a deadline is the narrowest limit that applies.</summary>
public class StepRunTests
{
    [Fact]
    public void Every_status_has_a_distinct_lower_case_name_that_is_read_back()
    {
        var tags = Enum.GetValues<StepRunStatus>().Select(status => status.Tag()).ToList();

        Assert.Equal(tags.Count, tags.Distinct().Count());
        Assert.All(tags, tag => Assert.Matches("^[a-z]+$", tag));
        Assert.All(Enum.GetValues<StepRunStatus>(), status =>
        {
            Assert.True(StepRunStatuses.TryParse(status.Tag(), out var parsed));
            Assert.Equal(status, parsed);
        });
        Assert.False(StepRunStatuses.TryParse("finished", out _));
        Assert.False(StepRunStatuses.TryParse(null, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((StepRunStatus)99).Tag());
    }

    [Theory]
    [InlineData(StepRunStatus.Started, new[] { StepRunStatus.Planned })]
    [InlineData(StepRunStatus.Ended, new[] { StepRunStatus.Planned, StepRunStatus.Started })]
    [InlineData(StepRunStatus.Checkpointed, new[] { StepRunStatus.Ended })]
    [InlineData(StepRunStatus.Concluded, new[] { StepRunStatus.Ended, StepRunStatus.Checkpointed })]
    [InlineData(StepRunStatus.Planned, new StepRunStatus[0])]
    public void A_run_moves_only_forward_from_the_statuses_that_lead_to_the_new_one(StepRunStatus to, StepRunStatus[] from)
    {
        Assert.Equal(from, to.MayMoveToFrom());
    }

    [Fact]
    public void A_run_that_never_recorded_its_start_ran_for_as_long_as_it_took_from_its_plan()
    {
        var planned = new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
        var run = new StepRun(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, StepKind.Validation, 0, StepRunStatus.Ended, planned.AddHours(1), planned, new string('a', 40), EndedAt: planned.AddMinutes(3), CommandIndex: 0);

        Assert.True(run.IsValidation);
        Assert.Equal(TimeSpan.FromMinutes(3), run.Duration);
        Assert.Equal(TimeSpan.FromMinutes(2), (run with { StartedAt = planned.AddMinutes(1) }).Duration);
        Assert.Null((run with { EndedAt = null }).Duration);
        Assert.False((run with { Kind = StepKind.Implementation }).IsValidation);
    }

    [Fact]
    public void The_keys_of_what_the_runtime_makes_for_a_step_run_are_the_Task_the_cause_and_what_is_made()
    {
        var task = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
        var step = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
        var cause = Hephaestus.Core.Effects.EffectKeys.StepCause(step);

        Assert.Equal($"{task:D}:{cause}:container:0", RunKeys.Container(task, step));
        Assert.Equal($"{task:D}:{cause}:volume:0", RunKeys.Volume(task, step));
        Assert.Equal($"{task:D}:{cause}:checkout:0", RunKeys.Checkout(task, step));
        Assert.Equal($"{task:D}:{cause}:checkpoint:0", RunKeys.Checkpoint(task, step));
    }

    [Fact]
    public void The_time_an_Agent_step_may_run_is_the_Project_limit_held_to_the_installation_limit_else_the_installation_limit_else_the_default()
    {
        var project = new Hephaestus.Core.Projects.ProjectLimits(TimeSpan.FromHours(1), null, null);

        Assert.Equal(TimeSpan.FromHours(1), StepDeadlines.AgentLimit(project, Hephaestus.Core.Limits.InstallationLimits.None));
        Assert.Equal(TimeSpan.FromMinutes(20), StepDeadlines.AgentLimit(project, new Hephaestus.Core.Limits.InstallationLimits(TimeSpan.FromMinutes(20), null, null)));
        Assert.Equal(TimeSpan.FromHours(1), StepDeadlines.AgentLimit(project, new Hephaestus.Core.Limits.InstallationLimits(TimeSpan.FromHours(3), null, null)));
        Assert.Equal(TimeSpan.FromHours(3), StepDeadlines.AgentLimit(Hephaestus.Core.Projects.ProjectLimits.None, new Hephaestus.Core.Limits.InstallationLimits(TimeSpan.FromHours(3), null, null)));
        Assert.Equal(StepDeadlines.DefaultAgentLimit, StepDeadlines.AgentLimit(Hephaestus.Core.Projects.ProjectLimits.None, Hephaestus.Core.Limits.InstallationLimits.None));
    }

    [Fact]
    public void The_Task_limit_is_the_same_limit_as_an_Agent_steps_and_a_step_is_stopped_at_the_narrowest_of_its_own_and_what_the_Task_has_left()
    {
        var now = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
        var project = new Hephaestus.Core.Projects.ProjectLimits(TimeSpan.FromHours(2), null, null);
        var limit = StepDeadlines.TaskLimit(project, Hephaestus.Core.Limits.InstallationLimits.None);

        Assert.Equal(TimeSpan.FromHours(2), limit);
        Assert.Equal(limit, StepDeadlines.AgentLimit(project, Hephaestus.Core.Limits.InstallationLimits.None));
        Assert.Equal(now + TimeSpan.FromHours(2), StepDeadlines.For(now, limit, limit, TimeSpan.Zero));
        Assert.Equal(now + TimeSpan.FromMinutes(30), StepDeadlines.For(now, TimeSpan.FromMinutes(30), limit, TimeSpan.FromMinutes(10)));
        Assert.Equal(now + TimeSpan.FromMinutes(50), StepDeadlines.For(now, limit, limit, TimeSpan.FromMinutes(70)));
        Assert.Equal(TimeSpan.FromMinutes(50), StepDeadlines.Remaining(limit, TimeSpan.FromMinutes(70)));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(121)]
    [InlineData(500)]
    public void A_Task_that_has_spent_all_its_time_gets_a_deadline_that_has_passed_and_never_one_before_now(int minutesSpent)
    {
        var now = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal(now, StepDeadlines.For(now, TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromMinutes(minutesSpent)));
    }
}
