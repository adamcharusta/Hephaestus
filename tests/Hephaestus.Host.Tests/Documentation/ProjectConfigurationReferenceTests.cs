using System.Text;
using System.Text.RegularExpressions;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;
using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Documentation;

/// <summary>Story 1.11, FR-5: the reference describes every key and gives complete examples, each with a stock image, that the parser accepts.</summary>
public partial class ProjectConfigurationReferenceTests
{
    private static readonly string Reference = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "docs", "project-configuration.md"));

    private static readonly ConfigurationContext Installation = new(
        [
            Alias.ForModel("opus5.5", Agent.ClaudeCode, "claude-opus-5-5", DateTimeOffset.UnixEpoch),
            Alias.ForModel("sol6.2", Agent.CodexCli, "gpt-6.2", DateTimeOffset.UnixEpoch),
            Alias.ForEffort("medium", Effort.Medium, DateTimeOffset.UnixEpoch),
        ],
        new InstallationLimits(TimeSpan.FromHours(4), 4000, 8L * 1024 * 1024 * 1024),
        ["default"]);

    private static List<(string Heading, string Yaml)> Examples()
    {
        var examples = Reference[Reference.IndexOf("## Examples", StringComparison.Ordinal)..];
        return [.. ExampleBlock().Matches(examples).Select(m => (m.Groups["heading"].Value, m.Groups["yaml"].Value))];
    }

    [GeneratedRegex(@"### (?<heading>[^\n]+)\n\n```yaml\n(?<yaml>.*?)```", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExampleBlock();

    [Fact]
    public void Every_key_the_parser_accepts_is_described()
    {
        var described = ProjectConfigurationFile.Keys
            .Concat(ProjectConfigurationFile.LimitKeys.Select(key => $"{ProjectConfigurationFile.LimitsKey}.{key}"))
            .Where(key => !Reference.Contains($"| `{key}` |", StringComparison.Ordinal))
            .ToList();

        Assert.True(described.Count == 0, $"Keys the reference does not describe: {string.Join(", ", described)}");
    }

    [Fact]
    public void The_reference_lists_no_key_the_parser_would_refuse()
    {
        var known = ProjectConfigurationFile.Keys.Concat(ProjectConfigurationFile.LimitKeys.Select(key => $"{ProjectConfigurationFile.LimitsKey}.{key}")).ToHashSet();
        var rows = Regex.Matches(Reference, @"^\| `(?<key>[A-Za-z.]+)` \|", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(m => m.Groups["key"].Value);

        Assert.All(rows, key => Assert.Contains(key, known));
    }

    [Fact]
    public void There_are_complete_examples_for_at_least_two_different_technology_stacks_each_with_its_own_stock_image()
    {
        var examples = Examples();
        var images = examples.Select(e => ProjectConfigurationParser.Parse(Encoding.UTF8.GetBytes(e.Yaml), Installation).Configuration!.Image).ToList();

        Assert.True(examples.Count >= 2);
        Assert.True(images.Distinct().Count() >= 2);
        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0", images);
        Assert.Contains("node:22", images);
    }

    [Fact]
    public void Every_example_is_accepted_by_the_parser()
    {
        foreach (var (heading, yaml) in Examples())
        {
            var parse = ProjectConfigurationParser.Parse(Encoding.UTF8.GetBytes(yaml), Installation);

            Assert.True(parse.IsValid, $"{heading}: {string.Join("; ", parse.Issues.Select(i => i.Setting + " " + i.Reason.Id))}");
        }
    }

    [Fact]
    public void One_example_sets_every_key()
    {
        var all = Examples().Select(e => ProjectConfigurationParser.Parse(Encoding.UTF8.GetBytes(e.Yaml), Installation).Configuration!).Single(c => c.PrimaryAlias is not null && c.FallbackAlias is not null);

        Assert.NotNull(all.Effort);
        Assert.NotNull(all.Language);
        Assert.NotNull(all.Limits.TaskDuration);
        Assert.NotNull(all.Limits.CpuMillis);
        Assert.NotNull(all.Limits.MemoryBytes);
        Assert.NotEmpty(all.AllowedEgressHosts);
        Assert.NotEmpty(all.Validation);
    }

    [Fact]
    public void The_README_points_to_the_reference_and_names_the_command()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "README.md"));

        Assert.Contains("docs/project-configuration.md", readme, StringComparison.Ordinal);
        Assert.Contains("project check acme/shop", readme, StringComparison.Ordinal);
    }
}
