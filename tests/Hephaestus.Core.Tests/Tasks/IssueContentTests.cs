using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Tasks;

/// <summary>AD-15: the hash is what a later comparison uses to notice that the content was edited.</summary>
public class IssueContentTests
{
    [Fact]
    public void The_hash_is_the_same_for_the_same_content_and_is_sixty_four_lower_case_hexadecimal_digits()
    {
        var hash = IssueContent.Hash("Title", "Body");

        Assert.Equal(hash, IssueContent.Hash("Title", "Body"));
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Theory]
    [InlineData("Title", "Body", "Title!", "Body")]
    [InlineData("Title", "Body", "Title", "Body ")]
    [InlineData("Title", "Body", "title", "Body")]
    [InlineData("Ab", "c", "A", "bc")]
    [InlineData("A", "", "", "A")]
    public void Any_edit_or_move_of_text_between_the_title_and_the_body_changes_the_hash(string title, string body, string otherTitle, string otherBody)
    {
        Assert.NotEqual(IssueContent.Hash(title, body), IssueContent.Hash(otherTitle, otherBody));
    }

    [Fact]
    public void The_text_is_hashed_as_it_was_given_with_no_normalization_of_line_endings()
    {
        Assert.NotEqual(IssueContent.Hash("T", "a\r\nb"), IssueContent.Hash("T", "a\nb"));
    }
}
