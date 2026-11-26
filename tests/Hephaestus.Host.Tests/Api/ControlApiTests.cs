using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using Hephaestus.Core;
using Hephaestus.Core.Localization;
using Hephaestus.Host.Cli;
using Hephaestus.Host.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Hephaestus.Host.Tests.Api;

public class ControlApiTests
{
    private static ControlApiClient ClientFor(RunningHost host, Language? language = null) =>
        new(host.Settings.ControlSocketPath, language ?? host.Settings.Language);

    [Fact]
    public async Task The_version_endpoint_answers_the_build_version_as_a_camel_case_document()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/version", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal($$"""{"version":"{{HephaestusVersion.Current}}"}""", answer.Body);
    }

    [Fact]
    public async Task The_control_api_listens_on_a_unix_socket_and_on_no_network_address()
    {
        // The aspnet image sets ASPNETCORE_HTTP_PORTS; an address named in the environment must not add a listener.
        await using var host = await RunningHost.StartAsync(configure: builder => builder.Configuration["HTTP_PORTS"] = "8080");

        var addresses = host.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;

        var address = Assert.Single(addresses);
        Assert.Equal($"http://unix:{host.Settings.ControlSocketPath}", address);
    }

    [Fact]
    public void A_kestrel_endpoint_from_configuration_is_refused_because_it_would_add_a_network_listener()
    {
        using var socket = new TempSocket();

        var exception = Assert.Throws<InvalidOperationException>(() => ControlHost.Build(
            new HostSettings(socket.Path, Language.English),
            builder => builder.Configuration["Kestrel:Endpoints:Http:Url"] = "http://127.0.0.1:0"));

        Assert.Contains("Unix socket only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_host_does_not_unlink_the_socket_of_a_running_one()
    {
        await using var host = await RunningHost.StartAsync();

        var exception = Assert.Throws<InvalidOperationException>(() => ControlHost.Build(host.Settings));

        Assert.Contains(host.Settings.ControlSocketPath, exception.Message, StringComparison.Ordinal);
        using var client = ClientFor(host);
        Assert.True((await client.GetAsync("/api/version", TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_socket_directory_the_host_creates_can_be_entered_by_its_owner_only()
    {
        using var parent = new TempSocket();
        var settings = new HostSettings(Path.Combine(Path.GetDirectoryName(parent.Path)!, "run", "control.sock"), Language.English);
        var app = ControlHost.Build(settings);
        await app.DisposeAsync();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(settings.ControlSocketPath)!));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task The_socket_is_readable_and_writable_by_its_owner_only()
    {
        await using var host = await RunningHost.StartAsync();
        await WaitForAsync(() => File.GetUnixFileMode(host.Settings.ControlSocketPath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(host.Settings.ControlSocketPath));
    }

    [Fact]
    public async Task A_stale_socket_file_from_an_earlier_run_does_not_stop_the_host()
    {
        using var socket = new TempSocket();
        await File.WriteAllTextAsync(socket.Path, "stale", TestContext.Current.CancellationToken);
        var app = ControlHost.Build(new HostSettings(socket.Path, Language.English));
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var client = new ControlApiClient(socket.Path, Language.English);
            var answer = await client.GetAsync("/api/version", TestContext.Current.CancellationToken);
            Assert.True(answer.IsSuccess);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_unknown_path_is_a_problem_document_in_the_installation_language()
    {
        await using var host = await RunningHost.StartAsync(Language.Polish);
        using var client = new ControlApiClient(host.Settings.ControlSocketPath, Language.Polish);

        var answer = await client.GetAsync("/api/nothing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, answer.Status);
        var problem = Assert.IsType<Hephaestus.Host.Api.ProblemDocument>(answer.Problem);
        Assert.Equal("urn:hephaestus:problem:not-found", problem.Type);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Nie znaleziono", problem.Title);
        Assert.Contains("/api/nothing", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_language_overrides_the_installation_language()
    {
        await using var host = await RunningHost.StartAsync(Language.Polish);
        using var client = ClientFor(host, Language.English);

        var answer = await client.GetAsync("/api/nothing", TestContext.Current.CancellationToken);

        Assert.Equal("Not found", answer.Problem!.Title);
    }

    [Fact]
    public async Task A_wrong_method_is_a_405_problem_document_naming_the_method_and_the_endpoint()
    {
        await using var host = await RunningHost.StartAsync();
        using var http = RawClient(host);

        using var response = await http.PostAsync("/api/version", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:hephaestus:problem:method-not-allowed", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("The endpoint /api/version does not accept the method POST.", document.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_unhandled_exception_is_a_500_problem_document_that_does_not_leak_the_exception()
    {
        await using var host = await RunningHost.StartAsync(mapMore: app => app.MapGet("/api/boom", Boom));
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/boom", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);
        Assert.Equal("urn:hephaestus:problem:unexpected", answer.Problem!.Type);
        Assert.Equal("Unexpected error", answer.Problem.Title);
        Assert.DoesNotContain("secret detail", answer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_failure_status_is_a_generic_problem_document()
    {
        await using var host = await RunningHost.StartAsync(mapMore: app => app.MapGet("/api/teapot", () => Results.StatusCode(418)));
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/teapot", TestContext.Current.CancellationToken);

        Assert.Equal("urn:hephaestus:problem:failed", answer.Problem!.Type);
        Assert.Equal(418, answer.Problem.Status);
        Assert.Contains("418", answer.Problem.Detail, StringComparison.Ordinal);
    }

    private static IResult Boom() => throw new InvalidOperationException("secret detail");

    private static HttpClient RawClient(RunningHost host) => new(new SocketsHttpHandler
    {
        ConnectCallback = async (_, cancellationToken) =>
        {
            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
            await socket.ConnectAsync(new System.Net.Sockets.UnixDomainSocketEndPoint(host.Settings.ControlSocketPath), cancellationToken);
            return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
        },
    })
    {
        BaseAddress = new Uri("http://hephaestus.control/"),
    };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
