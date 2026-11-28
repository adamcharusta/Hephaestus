using Hephaestus.Core.GitHub;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

/// <summary>Story 3.6, AD-9, FR-30: the committer of a checkpoint is the Hephaestus GitHub identity, as one line of text that cannot become a second header of the commit.</summary>
public class CommitIdentityTests
{
    private static GitHubIdentity App(string slug) => new(7, slug, "Hephaestus", "owner", [], [], []);

    [Fact]
    public void The_identity_of_the_app_is_its_bot_user_at_the_address_github_reads_as_that_user()
    {
        var identity = CommitIdentity.For(App("hephaestus-adam"));

        Assert.Equal("hephaestus-adam[bot]", identity.Name);
        Assert.Equal("hephaestus-adam[bot]@users.noreply.github.com", identity.Email);
    }

    [Fact]
    public void The_slug_the_loop_stores_of_the_app_is_all_the_identity_needs()
    {
        var identity = CommitIdentity.ForSlug("hephaestus-adam");

        Assert.Equal(("hephaestus-adam[bot]", "hephaestus-adam[bot]@users.noreply.github.com"), (identity.Name, identity.Email));
        Assert.Equal(CommitIdentity.For(App("hephaestus-adam")), identity);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" padded")]
    [InlineData("two\nlines")]
    [InlineData("angle<bracket")]
    [InlineData("angle>bracket")]
    [InlineData("tab\there")]
    public void A_name_or_an_address_that_is_not_one_plain_line_is_refused(string value)
    {
        Assert.Throws<ArgumentException>(() => new CommitIdentity(value, "a@example.test"));
        Assert.Throws<ArgumentException>(() => new CommitIdentity("A", value));
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("two@at@signs")]
    [InlineData("@host.test")]
    [InlineData("name@")]
    [InlineData("has space@host.test")]
    public void An_address_that_is_not_a_name_an_at_sign_and_a_host_is_refused(string email)
    {
        Assert.Throws<ArgumentException>(() => new CommitIdentity("A", email));
    }

    [Fact]
    public void A_name_longer_than_the_bound_is_refused_and_one_of_the_bound_is_not()
    {
        Assert.Equal(100, new CommitIdentity(new string('a', 100), "a@example.test").Name.Length);
        Assert.Throws<ArgumentException>(() => new CommitIdentity(new string('a', 101), "a@example.test"));
    }

    [Fact]
    public void An_app_that_is_not_given_has_no_identity()
    {
        Assert.Throws<ArgumentNullException>(() => CommitIdentity.For(null!));
    }
}
