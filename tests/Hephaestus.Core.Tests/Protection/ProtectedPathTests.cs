using Hephaestus.Core.Protection;

namespace Hephaestus.Core.Tests.Protection;

/// <summary>Story 1.9, AD-19: a protected path is a safe glob; a new installation protects the repository's automation and Hephaestus's own configuration.</summary>
public class ProtectedPathTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_new_installation_protects_github_and_hephaestus_yml()
    {
        Assert.Equal([".github/**", "hephaestus.yml"], ProtectedPath.Defaults);
        Assert.All(ProtectedPath.Defaults, glob => Assert.True(ProtectedPath.IsValid(glob)));
    }

    [Theory]
    [InlineData(".github/**")]
    [InlineData("hephaestus.yml")]
    [InlineData("docs/**/*.md")]
    [InlineData("src/[a-c]*.cs")]
    [InlineData("Dockerfile")]
    [InlineData("a b/c d")]
    [InlineData("..hidden")]
    public void A_relative_glob_is_a_protected_path(string glob)
    {
        Assert.True(ProtectedPath.IsValid(glob));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/etc/passwd")]
    [InlineData("docs/")]
    [InlineData("a//b")]
    [InlineData("../outside")]
    [InlineData("docs/../secrets")]
    [InlineData("./docs")]
    [InlineData("docs\\file")]
    [InlineData("docs/file\n")]
    [InlineData("docs/\0")]
    [InlineData("docs/\u200B")]
    [InlineData("\u202Edocs")]
    [InlineData(" docs")]
    [InlineData("docs ")]
    public void A_glob_that_could_name_a_path_outside_the_repository_or_be_read_two_ways_is_refused(string? glob)
    {
        Assert.False(ProtectedPath.IsValid(glob));
    }

    [Fact]
    public void The_longest_glob_is_accepted_and_one_more_character_is_not()
    {
        Assert.True(ProtectedPath.IsValid(new string('a', ProtectedPath.MaximumLength)));
        Assert.False(ProtectedPath.IsValid(new string('a', ProtectedPath.MaximumLength + 1)));
    }

    [Fact]
    public async Task Adding_a_glob_lists_it_once_and_a_second_add_says_it_was_already_listed()
    {
        var store = new FakeInstallation();
        var service = new ProtectedPathService(store);

        var first = await service.AddAsync("docs/**", Token);
        var second = await service.AddAsync("docs/**", Token);

        Assert.True(first.Changed);
        Assert.Equal("docs/**", first.Glob);
        Assert.False(second.Changed);
        Assert.Equal(["docs/**"], store.ProtectedPaths);
    }

    [Fact]
    public async Task A_glob_out_of_shape_is_refused_on_add_and_nothing_is_stored()
    {
        var store = new FakeInstallation();
        var service = new ProtectedPathService(store);

        var result = await service.AddAsync("../x", Token);

        Assert.Equal(ProtectedPathFailure.InvalidGlob, result.Failure);
        Assert.Empty(store.ProtectedPaths);
    }

    [Fact]
    public async Task Removing_a_listed_glob_removes_it_and_an_unlisted_or_malformed_one_is_not_protected()
    {
        var store = new FakeInstallation();
        store.ProtectedPaths.Add(".github/**");
        var service = new ProtectedPathService(store);

        var removed = await service.RemoveAsync(".github/**", Token);
        var again = await service.RemoveAsync(".github/**", Token);
        var malformed = await service.RemoveAsync("../x", Token);

        Assert.Equal(".github/**", removed.Glob);
        Assert.Equal(ProtectedPathFailure.NotProtected, again.Failure);
        Assert.Equal(ProtectedPathFailure.NotProtected, malformed.Failure);
        Assert.Empty(store.ProtectedPaths);
    }
}
