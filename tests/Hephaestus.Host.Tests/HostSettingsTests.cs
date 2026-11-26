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
}
