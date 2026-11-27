using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

/// <summary>Story 3.4, AD-7, NFR-4: which hosts a step run may reach, and the one setting that is the installation's egress default.</summary>
public class EgressPolicyTests
{
    [Fact]
    public void The_egress_default_of_this_release_is_open_unless_a_list_is_set_and_is_one_constant()
    {
        Assert.Equal(EgressDefault.OpenUnlessListed, EgressPolicy.Default);
    }

    [Fact]
    public void A_project_with_no_list_may_reach_any_public_host()
    {
        Assert.Null(EgressPolicy.AllowedHosts([], null));
        Assert.Null(EgressPolicy.AllowedHosts([], ["api.anthropic.com"]));
    }

    [Fact]
    public void An_agent_container_may_reach_the_projects_hosts_and_the_hosts_its_invocation_declares()
    {
        var hosts = EgressPolicy.AllowedHosts(["registry.npmjs.org", "api.nuget.org"], ["api.anthropic.com", "statsig.anthropic.com"]);

        Assert.Equal(["api.anthropic.com", "api.nuget.org", "registry.npmjs.org", "statsig.anthropic.com"], hosts!);
    }

    [Fact]
    public void A_validation_container_which_runs_no_invocation_gets_the_projects_list_alone()
    {
        var hosts = EgressPolicy.AllowedHosts(["api.nuget.org"], null);

        Assert.Equal(["api.nuget.org"], hosts!);
    }

    [Fact]
    public void Hosts_are_lower_cased_trimmed_and_listed_once()
    {
        var hosts = EgressPolicy.AllowedHosts(["API.NuGet.org ", "api.nuget.org", ""], ["Api.Nuget.Org"]);

        Assert.Equal(["api.nuget.org"], hosts!);
    }

    [Fact]
    public void With_the_allowlist_as_the_default_a_project_with_no_list_reaches_only_what_its_agent_needs()
    {
        Assert.Equal(["api.anthropic.com"], EgressPolicy.AllowedHosts([], ["api.anthropic.com"], EgressDefault.AllowlistOnly)!);
        Assert.Empty(EgressPolicy.AllowedHosts([], null, EgressDefault.AllowlistOnly)!);
    }

    [Fact]
    public void The_address_of_the_proxy_is_one_fixed_host_name_and_port()
    {
        Assert.Equal("hephaestus-egress", EgressProxyAddress.Host);
        Assert.Equal(3128, EgressProxyAddress.Port);
        Assert.Equal(EgressProxyAddress.Url, $"http://{EgressProxyAddress.Host}:{EgressProxyAddress.Port}");
    }
}
