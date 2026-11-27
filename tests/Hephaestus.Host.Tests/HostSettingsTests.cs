using Hephaestus.Core.Localization;

namespace Hephaestus.Host.Tests;

public class HostSettingsTests
{
    [Fact]
    public void Without_variables_the_socket_is_the_images_and_the_language_is_English()
    {
        var settings = HostSettings.FromEnvironment(_ => null);

        Assert.Equal("/run/hephaestus/control.sock", settings.ControlSocketPath);
        Assert.Equal(Language.English, settings.Language);
    }

    [Fact]
    public void The_variables_name_the_socket_and_the_language()
    {
        var settings = HostSettings.FromEnvironment(name => name switch
        {
            "HEPHAESTUS_CONTROL_SOCKET" => "/tmp/other.sock",
            "HEPHAESTUS_LANGUAGE" => "pl",
            _ => null,
        });

        Assert.Equal("/tmp/other.sock", settings.ControlSocketPath);
        Assert.Equal(Language.Polish, settings.Language);
    }

    [Fact]
    public void A_blank_socket_variable_is_the_default()
    {
        var settings = HostSettings.FromEnvironment(name => name == "HEPHAESTUS_CONTROL_SOCKET" ? "  " : null);

        Assert.Equal(HostSettings.DefaultControlSocketPath, settings.ControlSocketPath);
    }

    [Fact]
    public void Without_variables_the_agents_volume_is_the_images_and_the_lock_is_beside_the_executable()
    {
        var settings = HostSettings.FromEnvironment(_ => null);

        Assert.Equal("/var/lib/hephaestus/agents", settings.AgentsDirectory);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "agents.lock"), settings.AgentsLockFile);
        Assert.Equal("/etc/ssl/certs/ca-certificates.crt", settings.CaBundleFile);
        Assert.True(File.Exists(settings.AgentsLockFile), "The build places deploy/agents.lock beside the executable.");
    }

    [Fact]
    public void The_variables_name_the_agents_volume_the_lock_and_the_certificate_bundle()
    {
        var settings = HostSettings.FromEnvironment(name => name switch
        {
            "HEPHAESTUS_AGENTS_DIR" => "/tmp/agents",
            "HEPHAESTUS_AGENTS_LOCK" => "/tmp/agents.lock",
            "HEPHAESTUS_CA_BUNDLE" => "/tmp/ca.crt",
            _ => null,
        });

        Assert.Equal("/tmp/agents", settings.AgentsDirectory);
        Assert.Equal("/tmp/agents.lock", settings.AgentsLockFile);
        Assert.Equal("/tmp/ca.crt", settings.CaBundleFile);
    }

    [Fact]
    public void The_installation_is_named_hephaestus_unless_a_variable_names_another()
    {
        Assert.Equal("hephaestus", HostSettings.FromEnvironment(_ => null).InstallationId);
        Assert.Equal("hephaestus", HostSettings.FromEnvironment(name => name == "HEPHAESTUS_INSTALLATION_ID" ? " " : null).InstallationId);
        Assert.Equal("dev-1", HostSettings.FromEnvironment(name => name == "HEPHAESTUS_INSTALLATION_ID" ? "dev-1" : null).InstallationId);
    }

    [Fact]
    public void The_egress_proxy_is_created_from_the_hephaestus_image_unless_a_variable_names_another()
    {
        Assert.Equal("ghcr.io/adamcharusta/hephaestus:latest", HostSettings.FromEnvironment(_ => null).EgressProxyImage);
        Assert.Equal(HostSettings.DefaultEgressProxyImage, HostSettings.FromEnvironment(name => name == "HEPHAESTUS_EGRESS_PROXY_IMAGE" ? "" : null).EgressProxyImage);
        Assert.Equal("hephaestus:dev", HostSettings.FromEnvironment(name => name == "HEPHAESTUS_EGRESS_PROXY_IMAGE" ? "hephaestus:dev" : null).EgressProxyImage);
    }
}
