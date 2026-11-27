using Hephaestus.Core.Projects;

namespace Hephaestus.Core.Tests.Projects;

/// <summary>Text taken from a configuration file is made safe to print.</summary>
public class ConfigurationTextTests
{
    [Fact]
    public void Control_format_and_separator_characters_become_a_question_mark()
    {
        Assert.Equal("a?b?c?d?e", ConfigurationText.Safe("a\u001bb\u202Ec\u2028d\u0085e"));
        Assert.Equal("tab?newline?", ConfigurationText.Safe("tab\tnewline\n"));
    }

    [Fact]
    public void Ordinary_text_including_non_ASCII_letters_and_emoji_is_kept()
    {
        Assert.Equal("zażółć gęślą jaźń 🔥", ConfigurationText.Safe("zażółć gęślą jaźń 🔥"));
    }

    [Fact]
    public void A_long_value_is_cut_with_an_ellipsis_at_the_bound_without_splitting_a_pair()
    {
        Assert.Equal(new string('x', 5) + "…", ConfigurationText.Safe(new string('x', 20), 5));
        Assert.Equal("🔥🔥…", ConfigurationText.Safe("🔥🔥🔥🔥", 2));
        Assert.Equal("abc", ConfigurationText.Safe("abc", 3));
    }

    [Fact]
    public void Nothing_is_an_empty_string() => Assert.Equal(string.Empty, ConfigurationText.Safe(null));
}
