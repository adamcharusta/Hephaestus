using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

/// <summary>Story 3.2, AD-7: a step container's processor and memory come from the Task's settings, within the installation's.</summary>
public class StepLimitsTests
{
    private const long Gibibyte = 1024L * 1024 * 1024;

    [Fact]
    public void A_task_that_asks_for_nothing_in_an_installation_that_limits_nothing_gets_the_defaults()
    {
        var limits = StepLimits.Resolve(ProjectLimits.None, InstallationLimits.None);

        Assert.Equal(new StepLimits(StepLimits.DefaultCpuMillis, StepLimits.DefaultMemoryBytes), limits);
    }

    [Fact]
    public void The_tasks_own_settings_are_used_when_the_installation_allows_them()
    {
        var limits = StepLimits.Resolve(new ProjectLimits(null, 1500, Gibibyte), new InstallationLimits(null, 4000, 8 * Gibibyte));

        Assert.Equal(new StepLimits(1500, Gibibyte), limits);
    }

    [Fact]
    public void The_installations_ceiling_caps_what_the_task_asks_for()
    {
        var limits = StepLimits.Resolve(new ProjectLimits(null, 16_000, 64 * Gibibyte), new InstallationLimits(null, 4000, 8 * Gibibyte));

        Assert.Equal(new StepLimits(4000, 8 * Gibibyte), limits);
    }

    [Fact]
    public void A_task_that_asks_for_nothing_gets_the_installations_ceiling_when_it_is_below_the_default()
    {
        var limits = StepLimits.Resolve(ProjectLimits.None, new InstallationLimits(null, 500, Gibibyte));

        Assert.Equal(new StepLimits(500, Gibibyte), limits);
    }

    [Fact]
    public void A_task_that_asks_for_nothing_gets_the_installations_limit_even_when_it_is_above_the_default()
    {
        var limits = StepLimits.Resolve(ProjectLimits.None, new InstallationLimits(null, 8000, 16 * Gibibyte));

        Assert.Equal(new StepLimits(8000, 16 * Gibibyte), limits);
    }
}
