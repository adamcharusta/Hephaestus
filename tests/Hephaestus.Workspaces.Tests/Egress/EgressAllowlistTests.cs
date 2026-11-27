using Hephaestus.Workspaces.Egress;

namespace Hephaestus.Workspaces.Tests.Egress;

/// <summary>Story 3.4, NFR-4: the list of hosts a step run may reach, as the proxy applies it and as it is handed to the proxy.</summary>
public class EgressAllowlistTests
{
    [Fact]
    public void No_list_allows_any_host()
    {
        Assert.True(EgressAllowlist.Open.IsOpen);
        Assert.True(EgressAllowlist.Open.Allows("anything.example"));
        Assert.True(EgressAllowlist.Parse(null).Allows("anything.example"));
    }

    [Fact]
    public void A_list_allows_the_hosts_on_it_in_any_case_and_no_other()
    {
        var list = EgressAllowlist.Of(["api.nuget.org", "Registry.NPMJS.org"]);

        Assert.False(list.IsOpen);
        Assert.True(list.Allows("api.nuget.org"));
        Assert.True(list.Allows("API.NUGET.ORG"));
        Assert.True(list.Allows("registry.npmjs.org."));
        Assert.False(list.Allows("nuget.org"));
        Assert.False(list.Allows("evil.api.nuget.org"));
        Assert.False(list.Allows("api.nuget.org.evil.example"));
    }

    [Fact]
    public void An_empty_list_allows_nothing()
    {
        var list = EgressAllowlist.Of([]);

        Assert.False(list.IsOpen);
        Assert.False(list.Allows("api.nuget.org"));
        Assert.False(EgressAllowlist.Parse(string.Empty).Allows("api.nuget.org"));
    }

    [Fact]
    public void The_list_goes_to_the_proxy_as_one_line_and_comes_back_the_same()
    {
        var line = EgressAllowlist.Serialize(["api.nuget.org", " Registry.npmjs.org", ""]);

        Assert.Equal("api.nuget.org,registry.npmjs.org", line);
        var parsed = EgressAllowlist.Parse(line);
        Assert.True(parsed.Allows("registry.npmjs.org"));
        Assert.False(parsed.Allows("example.com"));
    }

    [Fact]
    public void No_list_is_serialized_as_nothing_so_that_the_variable_is_not_set()
    {
        Assert.Null(EgressAllowlist.Serialize(null));
        Assert.Equal("HEPHAESTUS_EGRESS_ALLOWED_HOSTS", EgressAllowlist.VariableName);
    }
}
