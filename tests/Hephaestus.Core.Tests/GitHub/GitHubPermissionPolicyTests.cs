using Hephaestus.Core.GitHub;

namespace Hephaestus.Core.Tests.GitHub;

/// <summary>Story 1.7, AD-19, NFR-6: the permissions the App needs, and the ones it must not have.</summary>
public class GitHubPermissionPolicyTests
{
    private static readonly Dictionary<string, GitHubPermissionLevel> Exactly = new()
    {
        ["contents"] = GitHubPermissionLevel.Write,
        ["issues"] = GitHubPermissionLevel.Write,
        ["pull-requests"] = GitHubPermissionLevel.Write,
        ["metadata"] = GitHubPermissionLevel.Read,
        ["checks"] = GitHubPermissionLevel.Read,
        ["statuses"] = GitHubPermissionLevel.Read,
    };

    [Fact]
    public void The_required_permissions_are_the_six_of_the_architecture_in_the_order_the_report_lists_them()
    {
        Assert.Equal(
            [("contents", GitHubPermissionLevel.Write), ("issues", GitHubPermissionLevel.Write), ("pull-requests", GitHubPermissionLevel.Write), ("metadata", GitHubPermissionLevel.Read), ("checks", GitHubPermissionLevel.Read), ("statuses", GitHubPermissionLevel.Read)],
            GitHubPermissionPolicy.Required);
    }

    [Fact]
    public void Exactly_the_required_permissions_are_all_present_and_warn_of_nothing()
    {
        var (permissions, warnings) = GitHubPermissionPolicy.Evaluate(Exactly);

        Assert.All(permissions, p => Assert.True(p.Present));
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("contents")]
    [InlineData("issues")]
    [InlineData("pull-requests")]
    [InlineData("metadata")]
    [InlineData("checks")]
    [InlineData("statuses")]
    public void A_permission_that_is_not_granted_is_missing_and_the_others_are_not(string absent)
    {
        var granted = Exactly.Where(pair => pair.Key != absent).ToDictionary();

        var (permissions, _) = GitHubPermissionPolicy.Evaluate(granted);

        var missing = Assert.Single(permissions, p => !p.Present);
        Assert.Equal(absent, missing.Name);
        Assert.Equal(GitHubPermissionLevel.None, missing.Granted);
    }

    [Theory]
    [InlineData("contents")]
    [InlineData("issues")]
    [InlineData("pull-requests")]
    public void Read_is_not_enough_where_write_is_needed(string name)
    {
        var granted = new Dictionary<string, GitHubPermissionLevel>(Exactly) { [name] = GitHubPermissionLevel.Read };

        var (permissions, _) = GitHubPermissionPolicy.Evaluate(granted);

        var check = Assert.Single(permissions, p => p.Name == name);
        Assert.False(check.Present);
        Assert.Equal(GitHubPermissionLevel.Read, check.Granted);
        Assert.Equal(GitHubPermissionLevel.Write, check.Required);
    }

    [Theory]
    [InlineData("metadata", GitHubPermissionLevel.Write)]
    [InlineData("checks", GitHubPermissionLevel.Write)]
    [InlineData("statuses", GitHubPermissionLevel.Admin)]
    [InlineData("contents", GitHubPermissionLevel.Admin)]
    public void A_higher_level_than_needed_counts_as_present(string name, GitHubPermissionLevel level)
    {
        var granted = new Dictionary<string, GitHubPermissionLevel>(Exactly) { [name] = level };

        var (permissions, _) = GitHubPermissionPolicy.Evaluate(granted);

        Assert.True(Assert.Single(permissions, p => p.Name == name).Present);
    }

    [Theory]
    [InlineData("workflows", GitHubPermissionLevel.Write)]
    [InlineData("administration", GitHubPermissionLevel.Read)]
    [InlineData("organization-administration", GitHubPermissionLevel.Write)]
    public void Workflows_or_administration_at_any_level_is_warned_about(string name, GitHubPermissionLevel level)
    {
        var granted = new Dictionary<string, GitHubPermissionLevel>(Exactly) { [name] = level };

        var (permissions, warnings) = GitHubPermissionPolicy.Evaluate(granted);

        Assert.All(permissions, p => Assert.True(p.Present));
        Assert.Equal(new GitHubPermissionWarning(name, level), Assert.Single(warnings));
    }

    [Fact]
    public void A_permission_that_is_none_is_not_a_warning()
    {
        var granted = new Dictionary<string, GitHubPermissionLevel>(Exactly) { ["workflows"] = GitHubPermissionLevel.None };

        Assert.Empty(GitHubPermissionPolicy.Evaluate(granted).Warnings);
    }

    [Fact]
    public void An_identity_is_ready_only_when_every_required_permission_is_present()
    {
        var (all, _) = GitHubPermissionPolicy.Evaluate(Exactly);
        var (some, _) = GitHubPermissionPolicy.Evaluate(Exactly.Where(pair => pair.Key != "checks").ToDictionary());

        Assert.True(new GitHubIdentity(1, "app", "App", "owner", [], all, []).Ready);
        Assert.False(new GitHubIdentity(1, "app", "App", "owner", [], some, []).Ready);
    }

    [Fact]
    public void A_result_is_either_an_identity_or_a_failure()
    {
        var identity = new GitHubIdentity(1, "app", "App", "owner", [], [], []);

        Assert.Same(identity, GitHubIdentityResult.Succeeded(identity).Identity);
        Assert.Equal(GitHubIdentityFailure.None, GitHubIdentityResult.Succeeded(identity).Failure);
        var failed = GitHubIdentityResult.Failed(GitHubIdentityFailure.Rejected);
        Assert.Null(failed.Identity);
        Assert.Equal(GitHubIdentityFailure.Rejected, failed.Failure);
    }
}
