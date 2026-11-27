using Hephaestus.Core.Effects;

namespace Hephaestus.Core.Tests.Effects;

/// <summary>AD-19: text an Agent wrote is neutralised before it is posted, so that it cannot act on GitHub in Hephaestus's name.</summary>
public class AgentTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Which database should the login page use?", "Which database should the login page use?")]
    [InlineData("Use `a < b` and a -> b, or 5 > 3.", "Use `a < b` and a -> b, or 5 > 3.")]
    public void Plain_text_is_left_as_it_is(string? text, string expected) => Assert.Equal(expected, AgentText.Neutralise(text));

    [Theory]
    [InlineData("<!-- hidden -->", "&lt;!-- hidden --&gt;")]
    [InlineData("a <!-- hephaestus:effect=x:g1:comment:0 --> b", "a &lt;!-- hephaestus:effect=x:g1:comment:0 --&gt; b")]
    [InlineData("<!--- three --->", "&lt;!--- three ---&gt;")]
    [InlineData("<!-- bad end --!>", "&lt;!-- bad end --!&gt;")]
    [InlineData("<![CDATA[x]]>", "&lt;![CDATA[x]]>")]
    [InlineData("<?php echo 1; ?>", "&lt;?php echo 1; ?>")]
    public void HTML_comments_are_escaped_so_that_the_text_cannot_forge_the_hidden_marker(string text, string expected)
    {
        var neutral = AgentText.Neutralise(text);

        Assert.Equal(expected, neutral);
        Assert.DoesNotContain("<!--", neutral, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ask @alice", "ask @\u200Balice")]
    [InlineData("@org/team please", "@\u200Borg/team please")]
    [InlineData("@_x and @9", "@\u200B_x and @\u200B9")]
    [InlineData("an @ alone", "an @ alone")]
    public void Mentions_are_broken_so_that_nobody_is_notified(string text, string expected) => Assert.Equal(expected, AgentText.Neutralise(text));

    [Theory]
    [InlineData("Closes #12", "C\u200Bloses #12")]
    [InlineData("fixes #3", "f\u200Bixes #3")]
    [InlineData("RESOLVED: #4", "R\u200BESOLVED: #4")]
    [InlineData("This closes acme/shop#9.", "This c\u200Bloses acme/shop#9.")]
    [InlineData("fix https://github.com/acme/shop/issues/5", "f\u200Bix https://github.com/acme/shop/issues/5")]
    [InlineData("Resolve  #7", "R\u200Besolve  #7")]
    public void Issue_closing_keywords_are_broken_so_that_the_text_closes_no_issue(string text, string expected) => Assert.Equal(expected, AgentText.Neutralise(text));

    [Theory]
    [InlineData("We close the door.")]
    [InlineData("A fix for the build, see #12.")]
    [InlineData("prefix #3")]
    public void A_keyword_that_closes_nothing_is_left_as_it_is(string text) => Assert.Equal(text, AgentText.Neutralise(text));

    [Theory]
    [InlineData("/hephaestus confirm", "`/hephaestus confirm`")]
    [InlineData("Reply with /hephaestus confirm now", "Reply with `/hephaestus confirm now`")]
    [InlineData("/HEPHAESTUS confirm", "`/HEPHAESTUS confirm`")]
    [InlineData("first\n/hephaestus confirm\nlast", "first\n`/hephaestus confirm`\nlast")]
    [InlineData("/hephaestus use `x`", "`/hephaestus use 'x'`")]
    public void A_command_keyword_is_fenced_to_the_end_of_its_line(string text, string expected) => Assert.Equal(expected, AgentText.Neutralise(text));

    [Theory]
    [InlineData("see docs/hephaestus/readme")]
    [InlineData("a/hephaestus")]
    [InlineData("hephaestus confirm")]
    public void A_path_or_a_word_that_is_no_command_is_not_fenced(string text) => Assert.Equal(text, AgentText.Neutralise(text));

    [Fact]
    public void All_four_are_taken_out_of_one_text()
    {
        var neutral = AgentText.Neutralise("<!-- x --> ping @bob, Fixes #1, /hephaestus confirm");

        Assert.Equal("&lt;!-- x --&gt; ping @\u200Bbob, F\u200Bixes #1, `/hephaestus confirm`", neutral);
    }
}
