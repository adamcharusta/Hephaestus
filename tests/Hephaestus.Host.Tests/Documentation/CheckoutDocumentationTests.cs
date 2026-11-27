using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Documentation;

/// <summary>Story 3.5, AD-6, AD-9: a decision record names the mirror, the bundle, the tools container, and the base image that carries git 2.48; the documents the Owner reads say how the tools image is made available.</summary>
public class CheckoutDocumentationTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, relative));

    [Fact]
    public void A_decision_record_names_the_mirror_the_bundle_the_tools_container_the_token_and_the_base_image()
    {
        var record = Read("docs/adr/0022-checkout-from-a-bundle.md");

        Assert.Contains("Accepted", record, StringComparison.Ordinal);
        Assert.Contains("hephaestus-mirrors", record, StringComparison.Ordinal);
        Assert.Contains("GitProcess", record, StringComparison.Ordinal);
        Assert.Contains("GIT_CONFIG_VALUE_0", record, StringComparison.Ordinal);
        Assert.Contains("BundleReference", record, StringComparison.Ordinal);
        Assert.Contains("hephaestus-tools", record, StringComparison.Ordinal);
        Assert.Contains("10.0-resolute", record, StringComparison.Ordinal);
        Assert.Contains("2.48", record, StringComparison.Ordinal);
        Assert.Contains("0022-checkout-from-a-bundle.md", Read("docs/adr/README.md"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("deploy/README.md")]
    [InlineData("docs/step-containers.md")]
    public void The_documents_say_how_the_tools_image_and_the_mirrors_volume_are_provided(string document)
    {
        var text = Read(document);

        Assert.Contains("hephaestus-tools", text, StringComparison.Ordinal);
        Assert.Contains("hephaestus-mirrors", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("deploy/README.md")]
    [InlineData("docs/step-containers.md")]
    public void The_documents_name_the_variable_and_the_build_of_the_tools_image(string document)
    {
        var text = Read(document);

        Assert.Contains("--profile tools build", text, StringComparison.Ordinal);
        Assert.Contains("HEPHAESTUS_TOOLS_IMAGE", text, StringComparison.Ordinal);
    }
}
