using System.Diagnostics;

namespace Hephaestus.Workspaces.Tests;

/// <summary>The git of this machine and a shell, for the tests that run the checkout script itself; where there is none they are skipped, and continuous integration, which has both, does not skip.</summary>
internal static class LocalGit
{
    public static bool Available { get; } = Probe("git", "--version") && Probe("sh", "-c", "exit 0");

    private static bool Probe(string file, params string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Runs a program with a clean git environment (no system or user configuration) and returns what it printed; a failure is an exception.</summary>
    public static (int ExitCode, string Output) Run(string directory, string file, IEnumerable<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Test";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "test@example.test";
        foreach (var pair in environment ?? new Dictionary<string, string>())
        {
            start.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.Result + error.Result);
    }

    public static string Git(string directory, params string[] arguments)
    {
        var (exit, output) = Run(directory, "git", arguments);
        return exit == 0 ? output.Trim() : throw new InvalidOperationException($"git {string.Join(' ', arguments)} ended with {exit}: {output}");
    }

    /// <summary>A repository with one commit on <c>main</c>: a tracked file, a tracked script, and a <c>.gitignore</c> that ignores <c>node_modules</c>.</summary>
    public static string Repository(string directory)
    {
        Directory.CreateDirectory(directory);
        Git(directory, "init", "--quiet", "-b", "main");
        File.WriteAllText(Path.Combine(directory, "README.md"), "hello\n");
        File.WriteAllText(Path.Combine(directory, ".gitignore"), "node_modules/\n");
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        File.WriteAllText(Path.Combine(directory, "src", "app.txt"), "app\n");
        Git(directory, "add", "-A");
        Git(directory, "commit", "--quiet", "-m", "first");
        return Git(directory, "rev-parse", "HEAD");
    }

    /// <summary>A bundle of one ref of the repository, named so that it is one head.</summary>
    public static void Bundle(string repository, string file, string reference) => Git(repository, "bundle", "create", "--quiet", file, reference);
}
