using Hephaestus.Host.Hosting;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>Story 1.11, AD-14: a Methodology is available when the image ships a directory with a manifest for it.</summary>
public sealed class DirectoryMethodologyCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hephaestus-methodologies-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Make(string relative, string content = "name: x\n")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void A_directory_with_a_manifest_is_available_and_one_without_is_not()
    {
        Make("default/manifest.yml");
        Make("bmad/README.md");
        Make("zeta/manifest.yml");

        Assert.Equal(["default", "zeta"], new DirectoryMethodologyCatalog(_root).ListNames());
    }

    [Fact]
    public void A_directory_whose_name_is_not_a_Methodology_name_and_a_loose_file_are_not_available()
    {
        Make("Not Valid/manifest.yml");
        Make("UPPER/manifest.yml");
        Make("manifest.yml");
        Make("default/manifest.yml");

        Assert.Equal(["default"], new DirectoryMethodologyCatalog(_root).ListNames());
    }

    [Fact]
    public void Names_are_in_ascending_ordinal_order_and_a_missing_directory_has_none()
    {
        Make("b/manifest.yml");
        Make("a/manifest.yml");
        Make("a-b/manifest.yml");

        Assert.Equal(["a", "a-b", "b"], new DirectoryMethodologyCatalog(_root).ListNames());
        Assert.Empty(new DirectoryMethodologyCatalog(Path.Combine(_root, "absent")).ListNames());
    }

    [Fact]
    public void The_image_ships_the_default_Methodology_beside_the_executable()
    {
        var shipped = new DirectoryMethodologyCatalog(DirectoryMethodologyCatalog.DefaultDirectory).ListNames();

        Assert.Contains("default", shipped);
    }

    [Fact]
    public async Task A_Methodology_is_read_with_all_its_files_under_forward_slash_paths_in_ordinal_order()
    {
        Make("default/manifest.yml", "name: default\n");
        Make("default/implementation.md", "Do it.\n");
        Make("default/nested/deeper/note.txt", "x");

        var methodology = await new DirectoryMethodologyCatalog(_root).ReadAsync("default", TestContext.Current.CancellationToken);

        Assert.Equal("default", methodology!.Name);
        Assert.Equal(["implementation.md", "manifest.yml", "nested/deeper/note.txt"], methodology.Files.Select(file => file.Path));
        Assert.Equal("Do it.\n", System.Text.Encoding.UTF8.GetString(methodology.Files[0].Content));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("bmad")]
    [InlineData("../default")]
    [InlineData("Not Valid")]
    [InlineData("")]
    public async Task A_Methodology_that_is_not_available_is_not_read(string name)
    {
        Make("default/manifest.yml");
        Make("bmad/README.md");

        Assert.Null(await new DirectoryMethodologyCatalog(_root).ReadAsync(name, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_link_in_a_Methodology_is_not_followed()
    {
        Make("default/manifest.yml");
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, "secret");
        File.CreateSymbolicLink(Path.Combine(_root, "default", "link.txt"), outside);

        var methodology = await new DirectoryMethodologyCatalog(_root).ReadAsync("default", TestContext.Current.CancellationToken);

        Assert.Equal(["manifest.yml"], methodology!.Files.Select(file => file.Path));
    }

    [Fact]
    public async Task A_Methodology_with_too_many_files_is_refused()
    {
        Make("default/manifest.yml");
        for (var index = 0; index <= DirectoryMethodologyCatalog.MaximumFiles; index++)
        {
            Make($"default/f/{index}.txt", "x");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => new DirectoryMethodologyCatalog(_root).ReadAsync("default", TestContext.Current.CancellationToken));
    }
}
