using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Hephaestus.EndToEnd.Tests;

/// <summary>
/// Story 1.4 end to end, with the real executable as two processes: <c>hephaestus serve</c> and the command line calling it
/// over the control socket, as <c>docker exec hephaestus hephaestus version</c> does in the container.
/// </summary>
public sealed class RunningExecutableTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hx").FullName;
    private readonly List<string> _hostOutput = [];
    private Process? _host;

    private string SocketPath => Path.Combine(_directory, "control.sock");

    public ValueTask InitializeAsync()
    {
        _host = Start(["serve"], new Dictionary<string, string>
        {
            // An address from the environment must not give the API a network listener.
            ["ASPNETCORE_URLS"] = "http://127.0.0.1:0",
            ["ASPNETCORE_HTTP_PORTS"] = "8080",
        });
        _host.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (_hostOutput)
                {
                    _hostOutput.Add(e.Data);
                }
            }
        };
        _host.ErrorDataReceived += (_, _) => { };
        _host.BeginOutputReadLine();
        _host.BeginErrorReadLine();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_host is { HasExited: false })
        {
            _host.Kill(entireProcessTree: true);
            _host.WaitForExit();
        }

        _host?.Dispose();
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task The_version_command_reaches_the_host_over_the_socket_in_both_languages()
    {
        var english = await RunWhenReadyAsync(["version"]);
        var polish = await RunAsync(["version"], language: "pl");
        var json = await RunAsync(["version", "--json"]);
        var polishJson = await RunAsync(["version", "--json"], language: "pl");

        Assert.Equal(0, english.ExitCode);
        Assert.Matches(@"^Hephaestus version \d+\.\d+\.\d+\s*$", english.Output);
        Assert.Matches(@"^Hephaestus w wersji \d+\.\d+\.\d+\s*$", polish.Output);
        Assert.Equal(json, polishJson);
        using var document = JsonDocument.Parse(json.Output);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("version").ValueKind);
        Assert.Equal(string.Empty, json.Error);
    }

    [Fact]
    public async Task The_host_listens_on_the_unix_socket_only_and_logs_one_json_object_per_line()
    {
        await RunWhenReadyAsync(["version"]);

        string[] lines;
        lock (_hostOutput)
        {
            lines = [.. _hostOutput];
        }

        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }

        var listening = lines.Where(l => l.Contains("Now listening on", StringComparison.Ordinal)).ToList();
        var address = Assert.Single(listening);
        Assert.Contains($"http://unix:{SocketPath}", address, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Contains("http://127.0.0.1", StringComparison.Ordinal) && l.Contains("Now listening", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_command_is_exit_code_2_and_a_missing_host_is_exit_code_1()
    {
        var unknown = await RunWhenReadyAsync(["frobnicate"]);
        var elsewhere = await RunAsync(["version"], socket: Path.Combine(_directory, "nobody.sock"));

        Assert.Equal(2, unknown.ExitCode);
        Assert.Equal(string.Empty, unknown.Output);
        Assert.Equal(1, elsewhere.ExitCode);
        Assert.Equal(string.Empty, elsewhere.Output);
        Assert.Equal(2, elsewhere.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private async Task<Run> RunWhenReadyAsync(string[] args)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            Assert.False(_host!.HasExited, "The host exited before it answered.");
            if (File.Exists(SocketPath))
            {
                var run = await RunAsync(args);
                if (run.ExitCode != 1)
                {
                    return run;
                }
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The host did not answer on its socket.");
    }

    private async Task<Run> RunAsync(string[] args, string language = "en", string? socket = null)
    {
        using var process = Start(args, new Dictionary<string, string>
        {
            ["HEPHAESTUS_LANGUAGE"] = language,
            ["HEPHAESTUS_CONTROL_SOCKET"] = socket ?? SocketPath,
        });
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return new Run(process.ExitCode, await output, await error);
    }

    private Process Start(string[] args, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Hephaestus.Host.exe" : "Hephaestus.Host"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        // The apphost finds the runtime through DOTNET_ROOT; the runtime this test runs on is the one to use.
        start.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        start.Environment["HEPHAESTUS_CONTROL_SOCKET"] = SocketPath;
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        return Process.Start(start)!;
    }

    private sealed record Run(int ExitCode, string Output, string Error);
}
