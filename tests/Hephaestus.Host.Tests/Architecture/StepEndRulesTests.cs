using System.Text.RegularExpressions;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>AD-18, FR-19, Story 3.3: the runtime alone stops a container at its deadline, and the loop never waits for a step.</summary>
public partial class StepEndRulesTests
{
    private const string Watcher = "src/Hephaestus.Workspaces/StepWatcher.cs";

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string file) => Path.GetRelativePath(RepositoryRoot.Path, file).Replace('\\', '/');

    /// <summary>Text without its comment lines, which may talk about a call; only code can make it.</summary>
    private static string WithoutComments(string text) => string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string Code(string file) => WithoutComments(File.ReadAllText(file));

    [Fact]
    public void No_source_file_but_the_step_watcher_stops_or_kills_a_container()
    {
        var offenders = SourceFiles()
            .Where(file => Relative(file) != Watcher)
            .Where(file => Ends().IsMatch(Code(file)))
            .Select(Relative);

        Assert.Empty(offenders);
        Assert.Matches(Ends(), Code(Path.Combine(RepositoryRoot.Path, Watcher)));
    }

    [Theory]
    [InlineData("await docker.Containers.StopContainerAsync(id, parameters, token);")]
    [InlineData("await _docker.Containers.KillContainerAsync(id, new ContainerKillParameters(), token);")]
    [InlineData("docker.Containers.PauseContainerAsync(id)")]
    [InlineData("await docker.Containers.RestartContainerAsync(id, parameters, token);")]
    public void The_rule_catches_the_ways_a_container_could_be_ended_early(string line)
    {
        Assert.Matches(Ends(), line);
    }

    [Theory]
    [InlineData("await docker.Containers.StartContainerAsync(id, parameters, token);")]
    [InlineData("await docker.Containers.RemoveContainerAsync(id, parameters, token);")]
    [InlineData("// StopContainerAsync is for the watcher")]
    public void The_rule_does_not_mistake_other_calls_for_it(string line)
    {
        Assert.DoesNotMatch(Ends(), WithoutComments(line));
    }

    [Fact]
    public void The_loop_asks_the_runtime_only_for_what_returns_without_waiting_for_a_step()
    {
        var loopFiles = Directory.GetFiles(Path.Combine(RepositoryRoot.Path, "src/Hephaestus.Core/Loop"), "OrchestratorLoop*.cs");
        var loop = string.Concat(loopFiles.Select(Code));

        // A step's container is started and left (AD-18): the loop has no way to ask the runtime to wait for it, and never runs a probe, which runs to its end.
        var calls = RuntimeCalls().Matches(loop).Select(match => match.Groups[1].Value).Distinct().Order().ToList();

        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Contains(call, new[] { "ResolveImageAsync", "EnsureTaskVolumeAsync", "StartStepAsync", "CheckoutAsync", "CheckpointAsync", "StartValidationAsync" }));
    }

    [GeneratedRegex(@"\bruntime!?\.(\w+)\(")]
    private static partial Regex RuntimeCalls();

    [GeneratedRegex(@"\.(StopContainerAsync|KillContainerAsync|PauseContainerAsync|RestartContainerAsync)\(")]
    private static partial Regex Ends();
}
