using System.Text.RegularExpressions;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>
/// AD-9, Story 3.5: the orchestrator runs no git command, and starts no process of any kind, in a directory an Agent can write to. The one place a process is started is the gateway's
/// <c>GitProcess</c>, which runs git against a mirror under the mirrors directory; the Docker runtime reaches containers through the Engine's API and starts nothing on the host.
/// </summary>
public partial class GitMirrorRulesTests
{
    private const string Runner = "src/Hephaestus.GitHub/GitProcess.cs";

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string file) => Path.GetRelativePath(RepositoryRoot.Path, file).Replace('\\', '/');

    [Fact]
    public void No_process_is_started_anywhere_but_in_the_one_git_runner_of_the_gateway()
    {
        var offenders = SourceFiles().Where(file => Relative(file) != Runner).Where(file => ProcessStart().IsMatch(File.ReadAllText(file))).Select(Relative);

        Assert.Empty(offenders);
        Assert.Matches(ProcessStart(), File.ReadAllText(Path.Combine(RepositoryRoot.Path, Runner)));
    }

    [Fact]
    public void Git_is_named_as_an_executable_nowhere_outside_the_gateway()
    {
        var offenders = SourceFiles()
            .Where(file => !Relative(file).StartsWith("src/Hephaestus.GitHub/", StringComparison.Ordinal))
            .Where(file => GitExecutable().IsMatch(File.ReadAllText(file)))
            .Select(Relative);

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("Process.Start(\"sh\");")]
    [InlineData("var start = new ProcessStartInfo(\"x\");")]
    [InlineData("ProcessStartInfo start = new();")]
    [InlineData("Process p = new();")]
    [InlineData("using var process = new Process { StartInfo = start };")]
    public void The_rule_catches_the_ways_a_process_could_be_started(string line)
    {
        Assert.Matches(ProcessStart(), line);
    }

    [Fact]
    public void The_runner_works_in_the_mirrors_directory_names_a_git_directory_and_switches_hooks_off()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, Runner));

        Assert.Contains("WorkingDirectory = _mirrors", source, StringComparison.Ordinal);
        Assert.Contains("--git-dir=", source, StringComparison.Ordinal);
        Assert.Contains("core.hooksPath=/dev/null", source, StringComparison.Ordinal);
        Assert.Contains("--template=", source, StringComparison.Ordinal);
        Assert.Contains("GIT_CONFIG_NOSYSTEM", source, StringComparison.Ordinal);
        Assert.Contains("Environment.Clear()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mirrors_are_named_after_the_repository_id_so_that_no_name_from_outside_reaches_a_path()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, Runner));

        Assert.Contains("$\"{repositoryId}.git\"", source, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"Process\.Start\(|\bProcessStartInfo\b|\bnew\s+Process\s*[({]|\bProcess\s+\w+\s*=\s*new\s*\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProcessStart();

    [GeneratedRegex(@"""git""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitExecutable();
}
