using Hephaestus.Core.Secrets;

namespace Hephaestus.Core.Tests.Secrets;

/// <summary>What may be stored as a secret (Story 1.5): the name is a safe file name, and a value is long enough that masking it cannot hit ordinary text.</summary>
public class SecretPolicyTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("anthropic-api-key")]
    [InlineData("github_app_private_key")]
    [InlineData("a1-b2_c3")]
    public void A_lowercase_name_of_letters_digits_hyphens_and_underscores_is_valid(string name)
    {
        Assert.True(SecretPolicy.IsValidName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Upper")]
    [InlineData("-a")]
    [InlineData("_a")]
    [InlineData(".a")]
    [InlineData("a.b")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("../a")]
    [InlineData("a\n")]
    [InlineData("zażółć")]
    public void A_name_that_could_leave_the_secrets_directory_or_is_not_a_plain_word_is_invalid(string? name)
    {
        Assert.False(SecretPolicy.IsValidName(name));
    }

    [Fact]
    public void A_name_has_at_most_64_characters()
    {
        Assert.True(SecretPolicy.IsValidName(new string('a', SecretPolicy.MaximumNameLength)));
        Assert.False(SecretPolicy.IsValidName(new string('a', SecretPolicy.MaximumNameLength + 1)));
    }

    [Theory]
    [InlineData(null, SecretValueRefusal.Empty)]
    [InlineData("", SecretValueRefusal.Empty)]
    [InlineData("          ", SecretValueRefusal.Empty)]
    [InlineData("\n\n\n\n\n\n\n\n\n", SecretValueRefusal.Empty)]
    [InlineData("1234567", SecretValueRefusal.TooShort)]
    [InlineData("12345678", SecretValueRefusal.None)]
    [InlineData("a much longer secret value", SecretValueRefusal.None)]
    public void A_value_is_refused_when_it_is_blank_or_under_the_minimum_length(string? value, SecretValueRefusal expected)
    {
        Assert.Equal(expected, SecretPolicy.CheckValue(value));
    }

    [Theory]
    [InlineData("secret\uFFFDvalue")]
    [InlineData("\uFFFD\uFFFD\uFFFD\uFFFD\uFFFD\uFFFD\uFFFD\uFFFD")]
    public void A_value_with_the_replacement_character_was_not_text_and_is_refused(string value)
    {
        Assert.Equal(SecretValueRefusal.NotText, SecretPolicy.CheckValue(value));
    }

    [Fact]
    public void A_value_in_another_script_is_accepted()
    {
        Assert.Equal(SecretValueRefusal.None, SecretPolicy.CheckValue("zażółć gęślą jaźń 密码 пароль"));
    }

    [Fact]
    public void The_minimum_is_8_characters_and_the_maximum_is_65536()
    {
        Assert.Equal(8, SecretPolicy.MinimumValueLength);
        Assert.Equal(SecretValueRefusal.None, SecretPolicy.CheckValue(new string('x', 65_536)));
        Assert.Equal(SecretValueRefusal.TooLong, SecretPolicy.CheckValue(new string('x', 65_537)));
    }

    [Theory]
    [InlineData("value\n", "value")]
    [InlineData("value\r\n", "value")]
    [InlineData("value", "value")]
    [InlineData("value\n\n", "value\n")]
    [InlineData("va\nlue", "va\nlue")]
    [InlineData("value\r", "value\r")]
    [InlineData("", "")]
    public void One_trailing_line_break_is_dropped_and_nothing_else(string input, string expected)
    {
        Assert.Equal(expected, SecretPolicy.TrimTrailingLineBreak(input));
    }
}
