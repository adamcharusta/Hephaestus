using System.Diagnostics;

namespace Hephaestus.GitHub.Tests;

/// <summary>The git of this machine, to make the repository a mirror is fetched from and to look at what the gateway made; the tests that need it are skipped where there is none, and continuous integration has it.</summary>
internal static class TestGit
{
    public static bool Available { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public static string Run(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Test";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "test@example.test";
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return process.ExitCode == 0 ? output.Result.Trim() : throw new InvalidOperationException($"git {string.Join(' ', arguments)} ended with {process.ExitCode}: {output.Result}{error.Result}");
    }

    /// <summary>A repository with commits on <c>main</c> and a branch of a Task, and the ids of what it holds.</summary>
    public static (string Path, string Main, string First, string TaskBranchTip) Repository(string directory)
    {
        Directory.CreateDirectory(directory);
        Run(directory, "init", "--quiet", "-b", "main");
        File.WriteAllText(Path.Combine(directory, "README.md"), "hello\n");
        Run(directory, "add", "-A");
        Run(directory, "commit", "--quiet", "-m", "first");
        var first = Run(directory, "rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(directory, "second.txt"), "second\n");
        Run(directory, "add", "-A");
        Run(directory, "commit", "--quiet", "-m", "second");
        var main = Run(directory, "rev-parse", "HEAD");
        Run(directory, "branch", "hephaestus/1-task", first);
        return (directory, main, first, first);
    }
}
