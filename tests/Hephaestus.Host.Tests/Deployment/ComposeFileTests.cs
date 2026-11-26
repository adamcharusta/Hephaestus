using Hephaestus.Host.Tests.Architecture;
using YamlDotNet.Serialization;

namespace Hephaestus.Host.Tests.Deployment;

/// <summary>
/// What <c>deploy/compose.yaml</c> and <c>deploy/Dockerfile</c> promise (Story 1.4, AD-17): one container named
/// <c>hephaestus</c>, healthy when its API answers, publishing no port, with nothing that reaches the Docker host.
/// </summary>
public class ComposeFileTests
{
    private static string Deploy(string file) => Path.Combine(RepositoryRoot.Path, "deploy", file);

    private static Dictionary<object, object> Service()
    {
        var document = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(Deploy("compose.yaml")));
        var services = (Dictionary<object, object>)document["services"];
        return (Dictionary<object, object>)services["hephaestus"];
    }

    [Fact]
    public void The_file_defines_one_service_and_the_container_is_named_hephaestus()
    {
        var document = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(Deploy("compose.yaml")));

        Assert.Equal(["hephaestus"], ((Dictionary<object, object>)document["services"]).Keys.Cast<string>());
        Assert.Equal("hephaestus", Service()["container_name"]);
    }

    [Fact]
    public void The_container_publishes_no_port_and_exposes_none()
    {
        var service = Service();

        Assert.DoesNotContain("ports", service.Keys);
        Assert.DoesNotContain("expose", service.Keys);
        Assert.DoesNotContain("network_mode", service.Keys);
    }

    [Fact]
    public void The_container_has_a_health_check_that_calls_the_api_through_the_command_line()
    {
        var healthcheck = (Dictionary<object, object>)Service()["healthcheck"];

        Assert.Equal(["CMD", "hephaestus", "version", "--json"], ((List<object>)healthcheck["test"]).Cast<string>());
    }

    [Fact]
    public void The_language_of_the_installation_reaches_the_container_and_defaults_to_English()
    {
        var environment = (Dictionary<object, object>)Service()["environment"];

        Assert.Equal("${HEPHAESTUS_LANGUAGE:-en}", environment["HEPHAESTUS_LANGUAGE"]);
    }

    [Fact]
    public void The_image_is_built_by_every_up_so_the_container_matches_the_source()
    {
        Assert.Equal("build", Service()["pull_policy"]);
    }

    [Fact]
    public void The_container_is_not_privileged_drops_every_capability_and_mounts_only_its_own_named_volumes()
    {
        var service = Service();

        Assert.DoesNotContain("privileged", service.Keys);
        Assert.Equal(["ALL"], ((List<object>)service["cap_drop"]).Cast<string>());
        Assert.Equal(["no-new-privileges:true"], ((List<object>)service["security_opt"]).Cast<string>());
        Assert.All(((List<object>)service["volumes"]).Cast<string>(), mount => Assert.StartsWith("hephaestus-", mount, StringComparison.Ordinal));
        Assert.DoesNotContain("docker.sock", File.ReadAllText(Deploy("compose.yaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_image_is_built_from_the_repository_root_with_the_deploy_dockerfile()
    {
        var build = (Dictionary<object, object>)Service()["build"];

        Assert.Equal("..", build["context"]);
        Assert.Equal("deploy/Dockerfile", build["dockerfile"]);
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Deploy(""), (string)build["context"], (string)build["dockerfile"]))));
    }

    [Fact]
    public void The_image_runs_the_host_as_a_non_root_user_on_a_socket_and_exposes_no_port()
    {
        var dockerfile = File.ReadAllText(Deploy("Dockerfile"));

        Assert.Contains("ENTRYPOINT [\"hephaestus\", \"serve\"]", dockerfile, StringComparison.Ordinal);
        Assert.Contains("USER $APP_UID", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("EXPOSE", dockerfile, StringComparison.Ordinal);
        Assert.Contains("HEPHAESTUS_CONTROL_SOCKET=/run/hephaestus/control.sock", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_image_builds_for_both_architectures_from_the_build_machine()
    {
        var dockerfile = File.ReadAllText(Deploy("Dockerfile"));

        Assert.Contains("--platform=$BUILDPLATFORM", dockerfile, StringComparison.Ordinal);
        Assert.Contains("mv /app/Hephaestus.Host /app/hephaestus", dockerfile, StringComparison.Ordinal);
        Assert.Contains("amd64) echo x64", dockerfile, StringComparison.Ordinal);
        Assert.Contains("arm64) echo arm64", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_data_and_secrets_volumes_are_named_as_the_architecture_names_them_and_mounted_where_the_image_expects()
    {
        var document = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(Deploy("compose.yaml")));
        var volumes = (Dictionary<object, object>)document["volumes"];

        Assert.Equal(["hephaestus-data", "hephaestus-secrets"], volumes.Keys.Cast<string>().Order(StringComparer.Ordinal));
        Assert.All(volumes, volume => Assert.Equal(volume.Key, ((Dictionary<object, object>)volume.Value)["name"]));
        Assert.Equal(
            [$"hephaestus-data:{HostSettings.DefaultDataDirectory}", $"hephaestus-secrets:{HostSettings.DefaultSecretsDirectory}"],
            ((List<object>)Service()["volumes"]).Cast<string>());
    }

    [Fact]
    public void The_image_creates_the_data_and_secrets_directories_for_the_process_user_alone()
    {
        var dockerfile = File.ReadAllText(Deploy("Dockerfile"));

        Assert.Contains($"HEPHAESTUS_DATA_DIR={HostSettings.DefaultDataDirectory}", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"HEPHAESTUS_SECRETS_DIR={HostSettings.DefaultSecretsDirectory}", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"chown \"$APP_UID\" /run/hephaestus {HostSettings.DefaultDataDirectory} {HostSettings.DefaultSecretsDirectory}", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"chmod 700 /run/hephaestus {HostSettings.DefaultDataDirectory} {HostSettings.DefaultSecretsDirectory}", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_default_control_socket_is_the_one_the_image_creates()
    {
        Assert.Equal("/run/hephaestus/control.sock", HostSettings.DefaultControlSocketPath);
    }
}
