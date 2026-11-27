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
}
