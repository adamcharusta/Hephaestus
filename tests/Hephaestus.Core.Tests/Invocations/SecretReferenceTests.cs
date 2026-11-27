using Hephaestus.Core.Invocations;

namespace Hephaestus.Core.Tests.Invocations;

/// <summary>AD-10: an invocation names a secret and where it goes, never a value.</summary>
public class SecretReferenceTests
{
    [Fact]
    public void A_reference_to_an_environment_variable_names_the_secret_and_the_variable()
    {
        var reference = new SecretReference("anthropic-api-key", "ANTHROPIC_API_KEY");

        Assert.Equal("anthropic-api-key", reference.Name);
        Assert.Equal("ANTHROPIC_API_KEY", reference.Target);
        Assert.True(reference.IsEnvironmentVariable);
    }

    [Fact]
    public void A_reference_to_a_file_names_an_absolute_path()
    {
        var reference = new SecretReference("codex-auth", "/hephaestus/home/.codex/auth.json");

        Assert.False(reference.IsEnvironmentVariable);
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("Has Space")]
    public void A_name_that_is_not_a_secret_name_is_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => new SecretReference(name, "VARIABLE"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1VARIABLE")]
    [InlineData("WITH-DASH")]
    [InlineData("relative/path")]
    [InlineData("/a/../b")]
    [InlineData("/a\nb")]
    public void A_target_that_is_neither_a_variable_nor_an_absolute_path_is_refused(string target)
    {
        Assert.False(SecretReference.IsValidTarget(target));
        Assert.Throws<ArgumentException>(() => new SecretReference("secret-name", target));
    }

    [Fact]
    public void A_target_that_is_null_is_refused()
    {
        Assert.False(SecretReference.IsValidTarget(null));
    }
}
