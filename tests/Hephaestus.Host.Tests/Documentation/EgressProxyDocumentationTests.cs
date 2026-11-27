using Hephaestus.Core.Workspaces;
using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Documentation;

/// <summary>Story 3.4, AD-7, AR-67: the proxy's address is documented where the architecture says it is, and a decision record names the proxy and how it is run.</summary>
public class EgressProxyDocumentationTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, relative));

    [Fact]
    public void A_decision_record_names_the_proxy_how_it_is_started_how_hosts_reach_it_how_step_containers_are_kept_apart_and_its_address()
    {
        var record = Read("docs/adr/0021-egress-proxy.md");

        Assert.Contains("hephaestus egress-proxy", record, StringComparison.Ordinal);
        Assert.Contains("Accepted", record, StringComparison.Ordinal);
        Assert.Contains("HEPHAESTUS_EGRESS_ALLOWED_HOSTS", record, StringComparison.Ordinal);
        Assert.Contains("hephaestus-net-<step-run>", record, StringComparison.Ordinal);
        Assert.Contains($"{EgressProxyAddress.Host}:{EgressProxyAddress.Port}", record, StringComparison.Ordinal);
        Assert.Contains("free and open-source", record, StringComparison.Ordinal);
        Assert.Contains("0021-egress-proxy.md", Read("docs/adr/README.md"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/step-containers.md")]
    [InlineData("docs/project-configuration.md")]
    public void The_address_of_the_proxy_is_documented_as_part_of_the_versioned_public_surface(string document)
    {
        var text = Read(document);

        Assert.Contains(EgressProxyAddress.Url, text, StringComparison.Ordinal);
        Assert.Contains("public surface", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_documentation_lists_the_proxy_settings_Maven_and_Gradle_need_with_the_fixed_address()
    {
        var text = Read("docs/project-configuration.md");

        Assert.Contains($"-Dhttps.proxyHost={EgressProxyAddress.Host} -Dhttps.proxyPort={EgressProxyAddress.Port}", text, StringComparison.Ordinal);
        Assert.Contains("<proxy>", text, StringComparison.Ordinal);
    }
}
