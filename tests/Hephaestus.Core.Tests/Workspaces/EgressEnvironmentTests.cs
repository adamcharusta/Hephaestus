using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

/// <summary>Story 3.4, AD-7: what every step container is told about the proxy, and what it is not told.</summary>
public class EgressEnvironmentTests
{
    [Fact]
    public void The_standard_proxy_variables_are_set_in_both_cases_and_point_at_the_fixed_address()
    {
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy" })
        {
            Assert.Equal("http://hephaestus-egress:3128", EgressEnvironment.Variables[name]);
        }
    }

    [Fact]
    public void The_containers_own_loopback_is_excluded_in_both_cases_and_nothing_else_is()
    {
        Assert.Equal("localhost,127.0.0.1,::1", EgressEnvironment.Variables["NO_PROXY"]);
        Assert.Equal("localhost,127.0.0.1,::1", EgressEnvironment.Variables["no_proxy"]);
    }

    [Fact]
    public void Git_is_told_through_its_environment_to_fetch_github_over_https_where_an_ssh_address_is_given()
    {
        Assert.Equal("2", EgressEnvironment.Variables["GIT_CONFIG_COUNT"]);
        Assert.Equal("url.https://github.com/.insteadOf", EgressEnvironment.Variables["GIT_CONFIG_KEY_0"]);
        Assert.Equal("git@github.com:", EgressEnvironment.Variables["GIT_CONFIG_VALUE_0"]);
        Assert.Equal("url.https://github.com/.insteadOf", EgressEnvironment.Variables["GIT_CONFIG_KEY_1"]);
        Assert.Equal("ssh://git@github.com/", EgressEnvironment.Variables["GIT_CONFIG_VALUE_1"]);
    }

    [Fact]
    public void Nothing_specific_to_a_language_or_build_tool_is_set()
    {
        var names = EgressEnvironment.Variables.Keys.Select(name => name.ToUpperInvariant()).ToList();

        Assert.Equal(11, names.Count);
        Assert.All(names, name => Assert.True(name.Contains("PROXY", StringComparison.Ordinal) || name.StartsWith("GIT_CONFIG_", StringComparison.Ordinal), name));
        Assert.DoesNotContain(names, name => name.Contains("JAVA", StringComparison.Ordinal) || name.Contains("MAVEN", StringComparison.Ordinal) || name.Contains("GRADLE", StringComparison.Ordinal) || name.Contains("NPM", StringComparison.Ordinal) || name.Contains("PIP", StringComparison.Ordinal) || name.Contains("GO", StringComparison.Ordinal) || name.Contains("CARGO", StringComparison.Ordinal) || name.Contains("NUGET", StringComparison.Ordinal) || name.Contains("DOTNET", StringComparison.Ordinal));
    }
}
