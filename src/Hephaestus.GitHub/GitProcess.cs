using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Hephaestus.GitHub;

/// <summary>What a git process did: how it ended, what it printed (bounded), and whether it was killed for taking too long.</summary>
internal sealed record GitOutput(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// The one place Hephaestus starts a process (AD-9). Git runs here only against a bare mirror under the mirrors directory: the directory is named by <c>--git-dir</c>, the working
/// directory of the process is the mirrors directory itself, and a path outside it is refused before anything starts. Every run gets no hook (<c>core.hooksPath</c> off), no system or
/// user configuration, no prompt, and only the protocols it is allowed, and a clean environment of its own besides what the caller adds, which is where a token goes and nowhere else.
/// </summary>
internal sealed class GitProcess(GitMirrorOptions options)
{
    private const int OutputLimit = 64 * 1024;

    // What the host's environment may give a git process besides the clean one it is started with: the proxy and the certificates its network needs to reach GitHub, which the host's own
    // requests to GitHub use too. Nothing else of the host's environment reaches git.
    private static readonly string[] NetworkVariables = ["HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy", "NO_PROXY", "no_proxy", "SSL_CERT_FILE", "SSL_CERT_DIR", "GIT_SSL_CAINFO"];

    private readonly string _mirrors = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.MirrorsDirectory));

    /// <summary>The directory of the mirror of a repository: the id names it, so that no name from outside can.</summary>
    public string MirrorOf(long repositoryId) => Path.Combine(_mirrors, $"{repositoryId}.git");

    /// <summary>Whether the mirror exists: a directory with a <c>HEAD</c>, which <c>git init</c> writes first.</summary>
    public bool MirrorExists(long repositoryId) => File.Exists(Path.Combine(MirrorOf(repositoryId), "HEAD"));

    /// <summary>Removes a mirror that is not whole, so that it is made again; a mirror is a cache. Nothing outside the mirrors directory is ever removed.</summary>
    public void RemoveMirror(long repositoryId)
    {
        var path = MirrorOf(repositoryId);
        if (IsInside(path) && Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>Makes an empty bare mirror: no template, so no hooks directory, no sample hook, and no description or exclude file, and no working tree.</summary>
    public Task<GitOutput> InitAsync(long repositoryId, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_mirrors);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new GitOutput(-1, string.Empty, exception.Message, TimedOut: false));
        }

        return RunAsync(["init", "--bare", "--quiet", "--template=", "--", MirrorOf(repositoryId)], null, options.CommandTimeout, cancellationToken);
    }

    /// <summary>Runs git in a mirror. <paramref name="environment"/> is what is added to the clean environment of the process.</summary>
    public Task<GitOutput> InMirrorAsync(long repositoryId, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, TimeSpan timeout, CancellationToken cancellationToken) =>
        RunAsync([$"--git-dir={MirrorOf(repositoryId)}", .. arguments], environment, timeout, cancellationToken);

    private async Task<GitOutput> RunAsync(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, TimeSpan timeout, CancellationToken cancellationToken)
    {
        foreach (var argument in arguments)
        {
            // A path the gateway built from a repository id is inside the mirrors directory; anything that names another place is a bug, and nothing is started for it.
            if (argument.StartsWith("--git-dir=", StringComparison.Ordinal) && !IsInside(argument["--git-dir=".Length..]))
            {
                throw new InvalidOperationException("Git is run only in a mirror.");
            }
        }

        var start = new ProcessStartInfo(options.GitExecutable)
        {
            WorkingDirectory = _mirrors,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.Environment.Clear();
        start.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        start.Environment["HOME"] = _mirrors;
        start.Environment["LC_ALL"] = "C";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_ALLOW_PROTOCOL"] = options.AllowedProtocols;
        foreach (var name in NetworkVariables)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                start.Environment[name] = value;
            }
        }

        foreach (var pair in environment ?? new Dictionary<string, string>())
        {
            start.Environment[pair.Key] = pair.Value;
        }

        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.hooksPath=/dev/null");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.fsmonitor=false");
        // A garbage collection that git starts after a fetch runs inside this process, where the timeout and the kill reach it, and not detached beside the next command.
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("gc.autoDetach=false");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("maintenance.autoDetach=false");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return new GitOutput(-1, string.Empty, exception.Message, TimedOut: false);
        }

        // Nothing is written to git's input; closing it means a command that asks for a line gets the end of the file, not a wait.
        process.StandardInput.Close();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var output = ReadBoundedAsync(process.StandardOutput);
        var error = ReadBoundedAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            await Task.WhenAll(output, error);
            return new GitOutput(-1, await output, await error, TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }

        return new GitOutput(process.ExitCode, await output, await error, TimedOut: false);
    }

    private bool IsInside(string path)
    {
        var full = Path.GetFullPath(path);
        return full.StartsWith(_mirrors + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It ended between the look and the kill.
        }
    }

    /// <summary>Reads to the end and keeps the first part, so that a command that prints a lot is never held up for want of a reader, and never fills memory.</summary>
    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var kept = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            if (kept.Length < OutputLimit)
            {
                kept.Append(buffer, 0, Math.Min(read, OutputLimit - kept.Length));
            }
        }

        return kept.ToString();
    }
}
