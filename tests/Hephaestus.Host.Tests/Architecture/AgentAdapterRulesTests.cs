using System.Reflection;
using System.Text.RegularExpressions;
using Hephaestus.Agents;
using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>AD-10, AR-37, Story 3.8: an Agent adapter is pure. It describes an invocation and reads a stream; it starts no process and references nothing of Docker, and it needs nothing but Core.</summary>
public partial class AgentAdapterRulesTests
{
    private static IEnumerable<string> AdapterSources() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src", "Hephaestus.Agents"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Code(string file) => string.Join('\n', File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void The_adapter_project_has_sources_and_none_of_them_starts_a_process_or_names_Docker()
    {
        var sources = AdapterSources().ToList();

        Assert.NotEmpty(sources);
        Assert.Empty(sources.Where(file => StartsAProcess().IsMatch(Code(file)) || NamesDocker().IsMatch(Code(file))).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("using System.Diagnostics;")]
    [InlineData("var process = Process.Start(info);")]
    [InlineData("new ProcessStartInfo(\"claude\")")]
    [InlineData("using Hephaestus.Workspaces;")]
    [InlineData("using Docker.DotNet;")]
    [InlineData("var d = new DockerClientConfiguration();")]
    public void The_rule_catches_the_ways_an_adapter_could_start_a_process_or_reach_Docker(string line)
    {
        Assert.True(StartsAProcess().IsMatch(line) || NamesDocker().IsMatch(line));
    }

    [Fact]
    public void The_adapter_project_references_Core_alone_and_no_package()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Hephaestus.Agents", "Hephaestus.Agents.csproj"));

        Assert.Empty(PackageReference().Matches(project));
        Assert.Equal(["..\\Hephaestus.Core\\Hephaestus.Core.csproj"], ProjectReference().Matches(project).Select(match => match.Groups["path"].Value));
    }

    [Fact]
    public void The_assembly_of_the_adapter_references_no_assembly_that_starts_a_process_or_talks_to_Docker()
    {
        var references = typeof(ClaudeCodeAgentAdapter).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        Assert.Contains("Hephaestus.Core", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Docker", StringComparison.Ordinal) || name.StartsWith("Hephaestus.Workspaces", StringComparison.Ordinal) || name == "System.Diagnostics.Process");
    }

    [Fact]
    public void Every_type_the_adapter_assembly_offers_is_an_adapter_of_a_port_of_Core()
    {
        var exported = typeof(ClaudeCodeAgentAdapter).Assembly.GetExportedTypes();

        Assert.Contains(typeof(ClaudeCodeAgentAdapter), exported);
        Assert.All(exported, type => Assert.True(typeof(IAgentAdapter).IsAssignableFrom(type), $"{type.Name} is not an IAgentAdapter."));
    }

    [GeneratedRegex(@"System\.Diagnostics|\bProcess(StartInfo)?\b\s*(\.|\()|\bnew\s+Process", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex StartsAProcess();

    [GeneratedRegex(@"Docker|Hephaestus\.Workspaces", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex NamesDocker();

    [GeneratedRegex(@"<PackageReference\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex PackageReference();

    [GeneratedRegex(@"<ProjectReference\s+Include=""(?<path>[^""]+)""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ProjectReference();
}
