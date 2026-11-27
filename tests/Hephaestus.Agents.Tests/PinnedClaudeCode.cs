using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Aliases;

namespace Hephaestus.Agents.Tests;

/// <summary>
/// The Claude Code executable <c>deploy/agents.lock</c> pins for this machine, for the tests that start the real CLI with no credential (Story 3.8). It is the file
/// named by <c>HEPHAESTUS_CLAUDE_CODE</c>, or else the vendor's download, checked against the lock and kept in the temporary directory under its digest. Either way
/// it is the pinned file or the test fails: a file that does not match the lock is never run. Where there is none, the test is skipped, except in GitHub Actions, where it fails.
/// </summary>
internal static class PinnedClaudeCode
{
    public const string Variable = "HEPHAESTUS_CLAUDE_CODE";

    private static readonly HttpClient Vendor = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static async Task<string> LocateAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("The pinned builds of Claude Code that Hephaestus runs are Linux builds.");
        }

        var binary = Pinned();
        if (Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } given)
        {
            // A file that was named and is not there or not the pinned one is a mistake, never a reason to skip.
            return await VerifiedAsync(given, binary, cancellationToken);
        }

        try
        {
            var cached = Path.Combine(Path.GetTempPath(), "hephaestus-test-agents", binary.Sha256);
            if (File.Exists(cached) && await Sha256Async(cached, cancellationToken) == binary.Sha256)
            {
                return cached;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            var staging = $"{cached}.{Guid.NewGuid():N}";
            await using (var source = await Vendor.GetStreamAsync(binary.Url, cancellationToken))
            await using (var target = File.Create(staging))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            if (await Sha256Async(staging, cancellationToken) != binary.Sha256)
            {
                File.Delete(staging);
                throw new InvalidOperationException($"{binary.Url} is not the file deploy/agents.lock pins.");
            }

            File.Move(staging, cached, overwrite: true);
            return cached;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException && !cancellationToken.IsCancellationRequested)
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                throw;
            }

            Assert.Skip($"The pinned Claude Code {binary.Version} is not here and could not be downloaded: {exception.Message}");
            throw;
        }
    }

    private static AgentBinary Pinned()
    {
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? AgentArchitecture.Arm64 : AgentArchitecture.X64;
        var libc = Directory.Exists("/lib") && Directory.EnumerateFiles("/lib", "ld-musl-*").Any() ? AgentLibc.Musl : AgentLibc.Glibc;
        var pinned = AgentLock.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "deploy", "agents.lock")));
        return pinned.Binaries.Single(binary => binary.Agent == Agent.ClaudeCode && binary.Architecture == architecture && binary.Libc == libc);
    }

    private static async Task<string> VerifiedAsync(string path, AgentBinary binary, CancellationToken cancellationToken) =>
        await Sha256Async(path, cancellationToken) == binary.Sha256
            ? path
            : throw new InvalidOperationException($"{Variable} does not name the Claude Code {binary.Version} that deploy/agents.lock pins for this machine.");

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
