using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Validation;

/// <summary>Story 3.12, AD-6, AD-7, AD-13, FR-31: one command is one container run on a fresh checkout of the pushed tip, in the Task's image, with no credential and with the Project's egress list alone.</summary>
public class ValidationCommandRequestTests
{
    private const string Branch = "hephaestus/12-let-the-project-decide";
    private static readonly Guid TaskId = Guid.Parse("0197a8a0-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("0197a8a0-0000-7000-8000-000000000002");
    private static readonly DateTimeOffset Deadline = new(2026, 11, 28, 12, 30, 0, TimeSpan.Zero);
    private static readonly ResolvedImage Image = new("registry.test/app@sha256:" + new string('a', 64), "sha256:" + new string('a', 64), AgentArchitecture.X64, CLibrary.Glibc);
    private static readonly BundleReference Tip = new("task/tip.bundle", new string('b', 64), new string('c', 40));

    private static ValidationCommandRequest Request(string command = "dotnet test", IReadOnlyList<string>? hosts = null, string branch = Branch, BundleReference? tip = null) =>
        new(TaskId, StepRunId, branch, tip ?? Tip, Image, command, new StepLimits(2000, 4L * 1024 * 1024 * 1024), hosts ?? [], Deadline);

    [Fact]
    public void The_command_is_checked_out_fresh_from_the_bundle_of_the_pushed_tip_under_its_own_step_run()
    {
        var checkout = Request().Checkout();

        Assert.Equal(TaskId, checkout.TaskId);
        Assert.Equal(StepRunId, checkout.StepRunId);
        Assert.Equal(Branch, checkout.Branch);
        Assert.Same(Tip, checkout.Bundle);
    }

    [Fact]
    public void The_container_runs_the_command_through_bash_in_the_Task_image_at_the_recorded_digest_with_the_limits_and_deadline()
    {
        var container = Request("npm test -- --ci").Container();

        Assert.Equal(["bash", "-c", "npm test -- --ci"], container.Command);
        Assert.Equal(Image, container.Image);
        Assert.Equal(TaskId, container.TaskId);
        Assert.Equal(StepRunId, container.StepRunId);
        Assert.Equal(new StepLimits(2000, 4L * 1024 * 1024 * 1024), container.Limits);
        Assert.Equal(Deadline, container.Deadline);
        Assert.True(container.IsValidation);
    }

    [Fact]
    public void The_container_holds_no_credential_no_context_no_Methodology_and_no_environment()
    {
        var container = Request().Container();

        Assert.Null(container.SecretReferences);
        Assert.Empty(container.Context);
        Assert.Empty(container.Methodology);
        Assert.Empty(container.Environment);
    }

    [Fact]
    public void The_container_may_reach_the_Projects_listed_hosts_and_no_other()
    {
        Assert.Equal(["api.example.com", "registry.npmjs.org"], Request(hosts: ["Registry.npmjs.org", "api.example.com"]).Container().AllowedEgressHosts);
    }

    [Fact]
    public void With_no_list_the_Project_gets_the_installations_default_and_never_an_Agents_hosts()
    {
        Assert.Null(Request().Container().AllowedEgressHosts);
        Assert.Equal(EgressPolicy.AllowedHosts([], null), Request().Container().AllowedEgressHosts);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("hephaestus/")]
    [InlineData("")]
    public void Only_a_Task_branch_is_checked_out(string branch)
    {
        Assert.Throws<ArgumentException>(() => Request(branch: branch));
    }

    [Fact]
    public void The_tip_is_a_whole_bundle()
    {
        Assert.Throws<ArgumentException>(() => Request(tip: new BundleReference("task/tip.bundle", new string('b', 64), new string('c', 40), new string('d', 40))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_command_is_not_blank(string command)
    {
        Assert.Throws<ArgumentException>(() => Request(command));
    }

    [Fact]
    public void A_command_is_no_longer_than_the_configuration_allows()
    {
        Assert.Throws<ArgumentException>(() => Request(new string('x', 2001)));
        Assert.Equal(2000, Request(new string('x', 2000)).Command.Length);
    }

    [Fact]
    public void Validation_is_a_step_kind_that_is_never_checkpointed_so_no_checkpoint_can_be_asked_for_what_a_command_left_in_the_tree()
    {
        Assert.False(StepKind.Validation.IsCheckpointed());
        Assert.Equal([StepKind.Implementation, StepKind.Correction, StepKind.Rework], Enum.GetValues<StepKind>().Where(kind => kind.IsCheckpointed()));
    }
}
