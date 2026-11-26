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

    private string DataDirectory => Path.Combine(_directory, "data");

    private string SecretsDirectory => Path.Combine(_directory, "secrets");

    public ValueTask InitializeAsync()
    {
        StartHost();
        return ValueTask.CompletedTask;
    }

    private void StartHost()
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
    }

    private void StopHost()
    {
        if (_host is { HasExited: false })
        {
            _host.Kill(entireProcessTree: true);
            _host.WaitForExit();
        }

        _host?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        StopHost();
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
    public async Task A_secret_set_from_standard_input_is_in_no_database_file_no_log_line_and_no_output_and_survives_a_restart()
    {
        const string Value = "sk-test-0123456789-abcdefghij";
        var runs = new List<Run>();

        runs.Add(await RunWhenReadyAsync(["secret", "list"]));
        runs.Add(await RunAsync(["secret", "set", "anthropic-api-key"], input: Value + "\n"));
        runs.Add(await RunAsync(["secret", "set", "other-secret", "--json"], input: Value + "-other"));
        runs.Add(await RunAsync(["secret", "list"]));
        runs.Add(await RunAsync(["secret", "list", "--json"], language: "pl"));
        var refused = await RunAsync(["secret", "set", "refused", Value]);
        runs.Add(refused);

        Assert.Equal(2, refused.ExitCode);
        Assert.Equal(Value, await File.ReadAllTextAsync(Path.Combine(SecretsDirectory, "anthropic-api-key"), TestContext.Current.CancellationToken));
        Assert.Equal(["anthropic-api-key", "other-secret"], Directory.EnumerateFiles(SecretsDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        // The host dies without a goodbye and starts again on the same directories, as a restarted container does.
        StopHost();
        StartHost();
        var listed = await RunWhenReadyAsync(["secret", "list", "--json"]);
        runs.Add(listed);
        runs.Add(await RunAsync(["secret", "remove", "other-secret"]));
        var final = await RunAsync(["secret", "list", "--json"]);
        runs.Add(final);

        Assert.Equal("""{"secrets":[{"name":"anthropic-api-key"},{"name":"other-secret"}]}""", listed.Output.Trim());
        Assert.Equal("""{"secrets":[{"name":"anthropic-api-key"}]}""", final.Output.Trim());
        Assert.Equal(["anthropic-api-key"], Directory.EnumerateFiles(SecretsDirectory).Select(Path.GetFileName));

        var databaseFiles = Directory.EnumerateFiles(DataDirectory).ToList();
        Assert.Contains(databaseFiles, f => f.EndsWith("hephaestus.db", StringComparison.Ordinal));
        foreach (var file in databaseFiles)
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("0123456789-abcdefghij", System.Text.Encoding.Latin1.GetString(bytes.ToArray()), StringComparison.Ordinal);
        }

        string[] logs;
        lock (_hostOutput)
        {
            logs = [.. _hostOutput];
        }

        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, line => line.Contains("0123456789-abcdefghij", StringComparison.Ordinal));
        Assert.DoesNotContain(runs, run => (run.Output + run.Error).Contains("0123456789-abcdefghij", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_value_in_utf_8_reaches_the_file_byte_for_byte_whatever_the_locale()
    {
        const string Value = "zażółć gęślą jaźń 密码 пароль\n-----END-----";
        await RunWhenReadyAsync(["secret", "list"]);

        var run = await RunAsync(["secret", "set", "unicode-secret"], input: Value);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(Value), await File.ReadAllBytesAsync(Path.Combine(SecretsDirectory, "unicode-secret"), TestContext.Current.CancellationToken));
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

    private async Task<Run> RunAsync(string[] args, string language = "en", string? socket = null, string input = "")
    {
        using var process = Start(args, new Dictionary<string, string>
        {
            ["HEPHAESTUS_LANGUAGE"] = language,
            ["HEPHAESTUS_CONTROL_SOCKET"] = socket ?? SocketPath,
        });
        await process.StandardInput.WriteAsync(input.AsMemory(), TestContext.Current.CancellationToken);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return new Run(process.ExitCode, await output, await error);
    }

    private Process Start(string[] args, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Hephaestus.Host.exe" : "Hephaestus.Host"))
        {
            RedirectStandardInput = true,
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
        start.Environment["HEPHAESTUS_DATA_DIR"] = DataDirectory;
        start.Environment["HEPHAESTUS_SECRETS_DIR"] = SecretsDirectory;
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        return Process.Start(start)!;
    }

    private sealed record Run(int ExitCode, string Output, string Error);
}
