using Hephaestus.Core.Protection;

namespace Hephaestus.Core.Tests.Protection;

/// <summary>AD-19, Story 3.7: a glob protects whole paths from the root, ignoring case, and the two default entries protect what they are for under every spelling.</summary>
public class ProtectedPathMatcherTests
{
    private static readonly string[] Defaults = [".github/**", "hephaestus.yml"];

    [Theory]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData(".github/CODEOWNERS")]
    [InlineData(".GitHub/Workflows/CI.yml")]
    [InlineData(".github")]
    [InlineData("hephaestus.yml")]
    [InlineData("Hephaestus.yml")]
    [InlineData("HEPHAESTUS.YML")]
    public void The_default_entries_protect_what_they_are_for_in_every_spelling(string path)
    {
        Assert.True(ProtectedPathMatcher.IsProtected(path, Defaults));
    }

    [Theory]
    [InlineData("src/hephaestus.yml")]
    [InlineData("docs/.github/x")]
    [InlineData(".githubx/a")]
    [InlineData("github/workflows/ci.yml")]
    [InlineData("hephaestus.yml.bak")]
    [InlineData("README.md")]
    [InlineData("")]
    public void Anything_else_is_not_protected_and_a_glob_is_matched_from_the_root(string path)
    {
        Assert.False(ProtectedPathMatcher.IsProtected(path, Defaults));
    }

    [Theory]
    [InlineData("docs/**", "docs/a/b/c.md", true)]
    [InlineData("docs/**", "docs", true)]
    [InlineData("docs/**", "documents/a", false)]
    [InlineData("**/Dockerfile", "Dockerfile", true)]
    [InlineData("**/Dockerfile", "a/b/Dockerfile", true)]
    [InlineData("**/Dockerfile", "a/b/Dockerfile.dev", false)]
    [InlineData("src/*.cs", "src/a.cs", true)]
    [InlineData("src/*.cs", "src/a/b.cs", false)]
    [InlineData("src/?.cs", "src/a.cs", true)]
    [InlineData("src/?.cs", "src/ab.cs", false)]
    [InlineData("a/**/b/**", "a/b", true)]
    [InlineData("a/**/b/**", "a/x/y/b/z", true)]
    [InlineData("a/**/**/b", "a/b", true)]
    [InlineData("*", "a", true)]
    [InlineData("*", "a/b", false)]
    [InlineData("*.yml", ".yml", true)]
    [InlineData("a*b*c", "aXbYc", true)]
    [InlineData("a*b*c", "aXbY", false)]
    public void Stars_cross_no_slash_and_a_double_star_is_any_number_of_segments(string glob, string path, bool expected)
    {
        Assert.Equal(expected, ProtectedPathMatcher.IsProtected(path, [glob]));
    }

    [Fact]
    public void The_globs_that_protect_a_path_are_named_and_a_glob_that_is_not_valid_protects_nothing()
    {
        Assert.Equal([".github/**"], ProtectedPathMatcher.Matching(".github/x", Defaults));
        Assert.Equal(["a/**", "**/b"], ProtectedPathMatcher.Matching("a/b", ["a/**", "**/b", "c"]));
        Assert.Empty(ProtectedPathMatcher.Matching("a", ["../a", "", "a/", "/a"]));
    }

    [Fact]
    public void Nothing_listed_protects_nothing()
    {
        Assert.False(ProtectedPathMatcher.IsProtected(".github/x", []));
    }

    [Fact]
    public void A_null_path_or_list_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => ProtectedPathMatcher.Matching(null!, []));
        Assert.Throws<ArgumentNullException>(() => ProtectedPathMatcher.Matching("a", null!));
    }
}
