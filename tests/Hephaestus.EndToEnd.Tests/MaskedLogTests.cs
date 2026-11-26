using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Hephaestus.EndToEnd.Tests;

/// <summary>
/// Story 1.6 end to end, with the real executable: a stored secret that a log line of the running host contains, in plain or base64
/// form, is a mask on the host's standard output. The host logs the paths it was given, so the paths carry the value.
/// </summary>
public sealed class MaskedLogTests : IDisposable
{
    private const string Value = "secret-value";
    private const string ValueInBase64 = "c2VjcmV0LXZhbHVl";

    private readonly string _directory = Directory.CreateTempSubdirectory("hx").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task A_secret_in_a_line_the_host_writes_is_a_mask_on_its_standard_output()
    {
        var secrets = Path.Combine(_directory, "secrets");
        Directory.CreateDirectory(secrets);
        await File.WriteAllTextAsync(Path.Combine(secrets, "token"), Value, TestContext.Current.CancellationToken);
        var socket = Path.Combine(_directory, ValueInBase64, "control.sock");

        var lines = new List<string>();
        using var host = Start(socket, Path.Combine(_directory, $"data-{Value}"), secrets);
        host.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (lines)
                {
                    lines.Add(e.Data);
                }
            }
        };
        host.BeginOutputReadLine();
        host.BeginErrorReadLine();
        try
        {
            await WaitForAsync(() => File.Exists(socket) && Snapshot(lines).Any(l => l.Contains("Control API listening", StringComparison.Ordinal)));
        }
        finally
        {
            host.Kill(entireProcessTree: true);
            await host.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        var written = Snapshot(lines);
        Assert.Contains(written, l => l.Contains("Database ready at", StringComparison.Ordinal) && l.Contains("data-***/", StringComparison.Ordinal));
        Assert.Contains(written, l => l.Contains("Control API listening on", StringComparison.Ordinal) && l.Contains("/***/control.sock", StringComparison.Ordinal));
        Assert.DoesNotContain(written, l => l.Contains(Value, StringComparison.Ordinal) || l.Contains(ValueInBase64, StringComparison.Ordinal));
    }

    private static string[] Snapshot(List<string> lines)
    {
        lock (lines)
        {
            return [.. lines];
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The host did not start.");
    }

    private static Process Start(string socket, string data, string secrets)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Hephaestus.Host.exe" : "Hephaestus.Host"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("serve");
        start.StandardOutputEncoding = Encoding.UTF8;
        start.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        start.Environment["HEPHAESTUS_CONTROL_SOCKET"] = socket;
        start.Environment["HEPHAESTUS_DATA_DIR"] = data;
        start.Environment["HEPHAESTUS_SECRETS_DIR"] = secrets;
        return Process.Start(start)!;
    }
}
