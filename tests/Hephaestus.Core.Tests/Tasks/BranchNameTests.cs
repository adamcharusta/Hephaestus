using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Tasks;

/// <summary>FR-6, Story 2.2: the branch is <c>hephaestus/&lt;issue-number&gt;-&lt;slug&gt;</c>, and the slug holds lower-case ASCII letters, digits, and hyphens in a bounded length.</summary>
public class BranchNameTests
{
    [Theory]
    [InlineData(12, "Add a login page", "hephaestus/12-add-a-login-page")]
    [InlineData(7, "Fix  the   spacing --- please!", "hephaestus/7-fix-the-spacing-please")]
    [InlineData(3, "  leading and trailing  ", "hephaestus/3-leading-and-trailing")]
    [InlineData(4, "Release v1.2.3", "hephaestus/4-release-v1-2-3")]
    [InlineData(5, "UPPER and lower", "hephaestus/5-upper-and-lower")]
    [InlineData(6, "Zażółć gęślą jaźń", "hephaestus/6-zazolc-gesla-jazn")]
    [InlineData(8, "Naïve café", "hephaestus/8-naive-cafe")]
    [InlineData(9, "ŁÓDŹ Škoda Œuvre ÿ", "hephaestus/9-lodz-skoda-uvre-y")]
    [InlineData(10, "Alice's title", "hephaestus/10-alice-s-title")]
    public void The_slug_is_the_title_in_lower_case_ASCII_with_single_hyphens(int number, string title, string expected)
    {
        Assert.Equal(expected, BranchName.For(number, title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ???")]
    [InlineData("日本語のタイトル")]
    [InlineData("---")]
    public void A_title_that_yields_no_letter_or_digit_gives_the_name_with_the_number_only(string title)
    {
        Assert.Equal("hephaestus/42", BranchName.For(42, title));
    }

    [Fact]
    public void A_title_in_decomposed_form_reads_the_same_as_one_in_composed_form()
    {
        Assert.Equal(BranchName.Slug("za\u017c\u00f3\u0142\u0107 na\u00efve"), BranchName.Slug("zaz\u0307o\u0301\u0142c\u0301 nai\u0308ve"));
        Assert.Equal("a", BranchName.Slug("a\u0328"));
    }

    [Fact]
    public void Every_marked_letter_is_read_as_the_ASCII_letter_it_stands_for()
    {
        Assert.Equal(BranchName.MarkedLetters.Length, BranchName.PlainLetters.Length);
        for (var i = 0; i < BranchName.MarkedLetters.Length; i++)
        {
            Assert.Equal(BranchName.PlainLetters[i].ToString(), BranchName.Slug(BranchName.MarkedLetters[i].ToString()));
            Assert.Equal(BranchName.PlainLetters[i].ToString(), BranchName.Slug(char.ToUpperInvariant(BranchName.MarkedLetters[i]).ToString()));
        }
    }

    [Fact]
    public void The_slug_is_bounded_and_never_ends_in_a_hyphen()
    {
        var title = string.Join(' ', Enumerable.Repeat("word", 30));

        var slug = BranchName.Slug(title);

        Assert.True(slug.Length <= BranchName.MaximumSlugLength);
        Assert.False(slug.EndsWith('-'));
        Assert.StartsWith("word-word", slug);
    }

    [Fact]
    public void A_single_word_longer_than_the_bound_is_cut_at_the_bound()
    {
        Assert.Equal(new string('a', BranchName.MaximumSlugLength), BranchName.Slug(new string('A', 200)));
    }

    [Fact]
    public void A_separator_that_would_end_the_slug_at_the_bound_is_not_kept()
    {
        var title = new string('a', BranchName.MaximumSlugLength - 1) + " b";

        Assert.Equal(new string('a', BranchName.MaximumSlugLength - 1), BranchName.Slug(title));
    }

    [Theory]
    [InlineData("Add a login page")]
    [InlineData("Zażółć gęślą jaźń 日本語 !!!")]
    [InlineData("a/b\\c..d@{e}~^:?*[f]")]
    public void Whatever_the_title_the_name_is_a_valid_shape(string title)
    {
        Assert.Matches(@"^hephaestus/[0-9]+(-[a-z0-9]+(-[a-z0-9]+)*)?$", BranchName.For(9, title));
    }
}
