using Hephaestus.Core.Projects;

namespace Hephaestus.Core.Tests.Projects;

/// <summary>Story 1.10: the shapes of a repository's name and of a Base Branch, checked before GitHub is asked.</summary>
public class ProjectNameTests
{
    [Theory]
    [InlineData("acme/shop", "acme", "shop")]
    [InlineData("Acme-Inc/Shop.Web_v2", "Acme-Inc", "Shop.Web_v2")]
    [InlineData("a/b", "a", "b")]
    [InlineData("a/.github", "a", ".github")]
    [InlineData("octocat_acme/shop", "octocat_acme", "shop")]
    public void An_owner_and_a_repository_are_split(string name, string owner, string repository)
    {
        Assert.True(ProjectName.TryParse(name, out var parsedOwner, out var parsedRepository));
        Assert.Equal((owner, repository), (parsedOwner, parsedRepository));
        Assert.True(ProjectName.IsValid(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("shop")]
    [InlineData("acme/")]
    [InlineData("/shop")]
    [InlineData("acme/shop/extra")]
    [InlineData("-acme/shop")]
    [InlineData("acme-/shop")]
    [InlineData("_acme/shop")]
    [InlineData("acme_/shop")]
    [InlineData("ac me/shop")]
    [InlineData("acme/sh op")]
    [InlineData("acme/.")]
    [InlineData("acme/..")]
    [InlineData("acme/shop.git")]
    [InlineData("acme/shop?x=1")]
    [InlineData("acme/shop#1")]
    [InlineData("acme[bot]/shop")]
    [InlineData("acme/shöp")]
    public void A_name_that_is_not_owner_slash_repository_is_refused(string? name)
    {
        Assert.False(ProjectName.TryParse(name, out var owner, out var repository));
        Assert.Equal((string.Empty, string.Empty), (owner, repository));
    }

    [Fact]
    public void An_owner_over_39_characters_and_a_repository_over_100_are_refused()
    {
        Assert.True(ProjectName.IsValid(new string('a', 39) + "/" + new string('b', 100)));
        Assert.False(ProjectName.IsValid(new string('a', 40) + "/shop"));
        Assert.False(ProjectName.IsValid("acme/" + new string('b', 101)));
    }

    [Fact]
    public void Names_are_the_same_whatever_their_case()
    {
        Assert.True(ProjectName.SameName("Acme/Shop", "acme/shop"));
        Assert.False(ProjectName.SameName("acme/shop", "acme/store"));
    }

    [Theory]
    [InlineData("main")]
    [InlineData("develop")]
    [InlineData("release/1.0")]
    [InlineData("feature/a-b_c.d")]
    [InlineData("v1.2.3")]
    [InlineData("user@example")]
    [InlineData("a")]
    public void An_ordinary_branch_name_is_accepted(string branch) => Assert.True(ProjectName.IsValidBranch(branch));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@")]
    [InlineData("-main")]
    [InlineData("/main")]
    [InlineData("main/")]
    [InlineData("main.")]
    [InlineData("a..b")]
    [InlineData("a b")]
    [InlineData("a\tb")]
    [InlineData("a~b")]
    [InlineData("a^b")]
    [InlineData("a:b")]
    [InlineData("a?b")]
    [InlineData("a*b")]
    [InlineData("a[b")]
    [InlineData("a\\b")]
    [InlineData("a@{b")]
    [InlineData("a//b")]
    [InlineData("a/.hidden")]
    [InlineData("a/b.lock")]
    [InlineData("a\u200Bb")]
    [InlineData("a\u2066b")]
    public void A_branch_name_git_would_refuse_or_a_command_would_take_for_an_option_is_refused(string? branch) => Assert.False(ProjectName.IsValidBranch(branch));

    [Fact]
    public void A_branch_name_has_at_most_255_characters()
    {
        Assert.True(ProjectName.IsValidBranch(new string('a', 255)));
        Assert.False(ProjectName.IsValidBranch(new string('a', 256)));
    }
}
