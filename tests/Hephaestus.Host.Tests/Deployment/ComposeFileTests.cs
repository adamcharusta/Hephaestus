using Hephaestus.Host.Tests.Architecture;
using YamlDotNet.Serialization;

namespace Hephaestus.Host.Tests.Deployment;

/// <summary>
/// What <c>deploy/compose.yaml</c> and <c>deploy/Dockerfile</c> promise (Story 1.4, AD-17): one container named
/// <c>hephaestus</c>, healthy when its API answers, publishing no port, and holding the Docker socket (Story 3.2, AD-7) and no other path of the host.
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

        // The second is the build of the tools image, in a profile `up` does not start (Story 3.5).
        Assert.Equal(["hephaestus", "tools"], ((Dictionary<object, object>)document["services"]).Keys.Cast<string>());
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
    public void The_egress_proxy_is_created_from_the_same_image_the_service_runs_so_that_both_are_one_release()
    {
        var service = Service();
        var environment = (Dictionary<object, object>)service["environment"];

        Assert.Equal(service["image"], environment["HEPHAESTUS_EGRESS_PROXY_IMAGE"]);
        Assert.Equal("ghcr.io/adamcharusta/hephaestus:${HEPHAESTUS_IMAGE_TAG:-latest}", service["image"]);
    }

    [Fact]
    public void The_hephaestus_service_is_on_no_network_a_step_container_or_the_egress_proxy_is_on()
    {
        var document = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(Deploy("compose.yaml")));

        // The runtime makes the outbound network and each step run's network itself; the Compose file declares neither, so the service is on the project's default network alone.
        Assert.DoesNotContain("networks", document.Keys);
        Assert.DoesNotContain("networks", Service().Keys);
    }

    [Fact]
    public void The_container_is_not_privileged_drops_every_capability_and_mounts_its_own_named_volumes_and_the_docker_socket_and_nothing_else()
    {
        var service = Service();
        var mounts = ((List<object>)service["volumes"]).Cast<string>().ToList();

        Assert.DoesNotContain("privileged", service.Keys);
        Assert.Equal(["ALL"], ((List<object>)service["cap_drop"]).Cast<string>());
        Assert.Equal(["no-new-privileges:true"], ((List<object>)service["security_opt"]).Cast<string>());
        Assert.Equal(["/var/run/docker.sock:/var/run/docker.sock"], mounts.Where(mount => !mount.StartsWith("hephaestus-", StringComparison.Ordinal)));
        Assert.All(mounts, mount => Assert.False(mount.StartsWith('.') || mount.StartsWith('~') || (mount.StartsWith('/') && mount != "/var/run/docker.sock:/var/run/docker.sock"), $"{mount} is a host path."));
    }

    [Fact]
    public void The_docker_socket_is_mounted_into_the_hephaestus_service_and_into_no_other_and_no_task_volume_is_mounted_into_it()
    {
        var document = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(Deploy("compose.yaml")));
        var services = (Dictionary<object, object>)document["services"];
        var mounting = services.Where(service => ((Dictionary<object, object>)service.Value).TryGetValue("volumes", out var volumes) && ((List<object>)volumes).Cast<string>().Any(mount => mount.Contains("docker.sock", StringComparison.Ordinal)));

        Assert.Equal(["hephaestus"], mounting.Select(service => (string)service.Key));
        // A Task's volume is `hephaestus-ws-<task-id>`, created by the runtime at run time; the file names none, and no volume of it holds a task.
        Assert.DoesNotContain("hephaestus-ws", File.ReadAllText(Deploy("compose.yaml")), StringComparison.Ordinal);
        Assert.DoesNotContain(((Dictionary<object, object>)document["volumes"]).Keys.Cast<string>(), name => name.StartsWith("hephaestus-ws", StringComparison.Ordinal));
    }

    [Fact]
    public void The_process_user_joins_the_group_that_owns_the_socket_and_the_installation_is_named()
    {
        var service = Service();
        var environment = (Dictionary<object, object>)service["environment"];

        Assert.Equal(["${HEPHAESTUS_DOCKER_GID:-999}"], ((List<object>)service["group_add"]).Cast<string>());
        Assert.Equal("${HEPHAESTUS_INSTALLATION_ID:-hephaestus}", environment["HEPHAESTUS_INSTALLATION_ID"]);
        Assert.Equal("hephaestus", HostSettings.DefaultInstallationId);
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
    public void The_four_volumes_are_named_as_the_architecture_names_them_and_mounted_where_the_image_expects()
    {
        var document = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(Deploy("compose.yaml")));
        var volumes = (Dictionary<object, object>)document["volumes"];

        Assert.Equal(["hephaestus-agents", "hephaestus-data", "hephaestus-mirrors", "hephaestus-secrets"], volumes.Keys.Cast<string>().Order(StringComparer.Ordinal));
        Assert.All(volumes, volume => Assert.Equal(volume.Key, ((Dictionary<object, object>)volume.Value)["name"]));
        Assert.Equal(
            [
                $"hephaestus-data:{HostSettings.DefaultDataDirectory}",
                $"hephaestus-secrets:{HostSettings.DefaultSecretsDirectory}",
                $"hephaestus-agents:{HostSettings.DefaultAgentsDirectory}",
                $"hephaestus-mirrors:{HostSettings.DefaultMirrorsDirectory}",
                "/var/run/docker.sock:/var/run/docker.sock",
            ],
            ((List<object>)Service()["volumes"]).Cast<string>());
    }

    [Fact]
    public void The_image_creates_the_data_and_secrets_directories_for_the_process_user_alone()
    {
        // The long lines are continued; a continuation is one line.
        var dockerfile = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Deploy("Dockerfile")), @"\s*\\\r?\n\s*", " ");
        Assert.Equal(HostSettings.DefaultAgentsDirectory, Environment(dockerfile, "HEPHAESTUS_AGENTS_DIR"));

        Assert.Contains($"HEPHAESTUS_DATA_DIR={HostSettings.DefaultDataDirectory}", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"HEPHAESTUS_SECRETS_DIR={HostSettings.DefaultSecretsDirectory}", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"chown \"$APP_UID\" /run/hephaestus {HostSettings.DefaultDataDirectory} {HostSettings.DefaultSecretsDirectory} {HostSettings.DefaultAgentsDirectory} {HostSettings.DefaultMirrorsDirectory}", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"chmod 700 /run/hephaestus {HostSettings.DefaultDataDirectory} {HostSettings.DefaultSecretsDirectory} {HostSettings.DefaultMirrorsDirectory}", dockerfile, StringComparison.Ordinal);
        // The agents directory is the one the step containers read, as users of their own.
        Assert.Contains($"chmod 755 {HostSettings.DefaultAgentsDirectory}", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_default_control_socket_is_the_one_the_image_creates()
    {
        Assert.Equal("/run/hephaestus/control.sock", HostSettings.DefaultControlSocketPath);
    }

    [Fact]
    public void The_mirrors_volume_is_mounted_into_the_hephaestus_service_alone_and_the_image_names_its_directory()
    {
        var document = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(Deploy("compose.yaml")));
        var services = (Dictionary<object, object>)document["services"];
        var mounting = services.Where(service => ((Dictionary<object, object>)service.Value).TryGetValue("volumes", out var volumes) && ((List<object>)volumes).Cast<string>().Any(mount => mount.StartsWith("hephaestus-mirrors:", StringComparison.Ordinal)));

        Assert.Equal(["hephaestus"], mounting.Select(service => (string)service.Key));
        Assert.Equal(HostSettings.DefaultMirrorsDirectory, Environment(File.ReadAllText(Deploy("Dockerfile")), "HEPHAESTUS_MIRRORS_DIR"));
        Assert.Equal("/var/lib/hephaestus/mirrors", HostSettings.DefaultMirrorsDirectory);
    }

    [Fact]
    public void The_tools_image_is_built_by_a_service_in_a_profile_that_is_not_started_and_the_runtime_is_told_its_name()
    {
        var document = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(Deploy("compose.yaml")));
        var tools = (Dictionary<object, object>)((Dictionary<object, object>)document["services"])["tools"];
        var environment = (Dictionary<object, object>)Service()["environment"];

        Assert.Equal(["tools"], ((List<object>)tools["profiles"]).Cast<string>());
        Assert.Equal("build", tools["pull_policy"]);
        Assert.Equal("ghcr.io/adamcharusta/hephaestus-tools:${HEPHAESTUS_IMAGE_TAG:-latest}", tools["image"]);
        Assert.Equal(tools["image"], environment["HEPHAESTUS_TOOLS_IMAGE"]);
        var build = (Dictionary<object, object>)tools["build"];
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Deploy(""), (string)build["context"], (string)build["dockerfile"]))));
        Assert.DoesNotContain("volumes", tools.Keys);
        Assert.DoesNotContain("ports", tools.Keys);
    }

    [Fact]
    public void The_hephaestus_image_has_git_2_48_or_later_and_the_build_stops_when_it_has_not()
    {
        var dockerfile = File.ReadAllText(Deploy("Dockerfile"));

        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:10.0-resolute AS runtime", dockerfile, StringComparison.Ordinal);
        Assert.Contains("apt-get install --no-install-recommends --yes git ca-certificates", dockerfile, StringComparison.Ordinal);
        Assert.Contains("v[1] > 2 || (v[1] == 2 && v[2] >= 48)", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tools_image_holds_git_2_48_or_later_and_nothing_else()
    {
        var dockerfile = File.ReadAllText(Deploy("tools.Dockerfile"));
        var instructions = dockerfile.Split('\n').Where(line => !line.TrimStart().StartsWith('#') && line.Trim().Length > 0).ToList();

        Assert.Equal("FROM alpine:3.23", instructions[0]);
        Assert.Contains("apk add --no-cache git", dockerfile, StringComparison.Ordinal);
        Assert.Contains("v[1] > 2 || (v[1] == 2 && v[2] >= 48)", dockerfile, StringComparison.Ordinal);
        Assert.Matches(@"apk add --no-cache git\s*\\?\s*\n?\s*&&", dockerfile);
        Assert.DoesNotContain("COPY", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("ADD ", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("ENTRYPOINT", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("openssh", dockerfile, StringComparison.Ordinal);
        Assert.Equal("USER 10001:10001", instructions[^1]);
        Assert.Single(instructions, line => line.StartsWith("RUN", StringComparison.Ordinal));
    }

    private static string Environment(string dockerfile, string name) =>
        System.Text.RegularExpressions.Regex.Match(dockerfile, $@"{name}=(\S+)").Groups[1].Value;
}
