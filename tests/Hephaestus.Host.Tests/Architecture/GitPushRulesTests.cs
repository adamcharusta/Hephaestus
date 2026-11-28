using System.Text.RegularExpressions;
using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>
/// AD-4, AD-9, FR-44, AR-53, Story 3.7: <c>Hephaestus.GitHub</c> is the only code that pushes, and it has no code path that pushes with a plus or a force, mirrors, prunes, deletes a ref of the remote, or pushes anything that is not
/// the Task branch from a quarantine ref.
/// </summary>
public partial class GitPushRulesTests
{
    private const string Gateway = "src/Hephaestus.GitHub/GitHubMirrors.Push.cs";

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string file) => Path.GetRelativePath(RepositoryRoot.Path, file).Replace('\\', '/');

    /// <summary>The file without its comment lines, which may talk about a push; only code can make one.</summary>
    private static string Code(string file) => string.Join('\n', File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

    [Fact]
    public void No_source_file_but_the_gateways_push_names_the_git_push_command_and_git_is_run_nowhere_else()
    {
        // GitMirrorRulesTests shows that git is named as an executable in Hephaestus.GitHub alone, so a push command can only be made there; the tags of an effect and an intent are not git.
        var offenders = SourceFiles()
            .Where(file => Relative(file).StartsWith("src/Hephaestus.GitHub/", StringComparison.Ordinal) && Relative(file) != Gateway)
            .Where(file => GitPush().IsMatch(Code(file)))
            .Select(Relative);

        Assert.Empty(offenders);
        Assert.Matches(GitPush(), Code(Path.Combine(RepositoryRoot.Path, Gateway)));
    }

    [Theory]
    [InlineData("[\"push\", \"--porcelain\", url, spec]")]
    [InlineData("var arguments = new[] { \"push\" };")]
    [InlineData("Git(\"push\", x)")]
    public void The_rule_catches_the_ways_a_push_could_be_named(string line)
    {
        Assert.Matches(GitPush(), line);
    }

    [Theory]
    [InlineData("--force")]
    [InlineData("--force-with-lease")]
    [InlineData("--delete")]
    [InlineData("--mirror")]
    [InlineData("--prune")]
    [InlineData("--all")]
    [InlineData("--tags")]
    [InlineData("\"+refs")]
    [InlineData("\"-f\"")]
    [InlineData("\"-d\", \"refs/heads")]
    public void The_gateways_push_has_no_force_no_delete_no_mirror_and_no_plus(string forbidden)
    {
        Assert.DoesNotContain(forbidden, Code(Path.Combine(RepositoryRoot.Path, Gateway)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_one_push_has_one_refspec_from_the_quarantine_ref_to_the_Task_branch()
    {
        var code = Code(Path.Combine(RepositoryRoot.Path, Gateway));

        Assert.Single(PushCommand().Matches(code));
        Assert.Contains("$\"{quarantine}:refs/heads/{request.Branch}\"", code, StringComparison.Ordinal);
        Assert.Contains("[\"push\", \"--porcelain\", \"--no-follow-tags\", url, ", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_only_ref_the_gateway_deletes_is_the_quarantine_name()
    {
        var code = Code(Path.Combine(RepositoryRoot.Path, Gateway));

        var deletions = UpdateRefDelete().Matches(code);
        Assert.NotEmpty(deletions);
        Assert.All(deletions, deletion => Assert.Equal("quarantine", deletion.Groups["ref"].Value));
    }

    [Fact]
    public void The_Task_branches_port_is_implemented_by_the_gateway_alone()
    {
        var implementers = new[] { typeof(ITaskBranches).Assembly, typeof(Hephaestus.GitHub.GitHubPoller).Assembly, typeof(EntryPoint).Assembly, typeof(Hephaestus.Persistence.PersistenceOptions).Assembly, typeof(Hephaestus.Workspaces.WorkspaceRuntimeOptions).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true } && type.GetInterfaces().Contains(typeof(ITaskBranches)));

        Assert.Equal([typeof(Hephaestus.GitHub.GitHubMirrors)], implementers);
    }

    [Fact]
    public void The_pull_requests_port_is_implemented_by_the_gateway_alone_and_offers_nothing_that_merges_or_closes()
    {
        var implementers = new[] { typeof(IPullRequests).Assembly, typeof(Hephaestus.GitHub.GitHubPoller).Assembly, typeof(EntryPoint).Assembly, typeof(Hephaestus.Persistence.PersistenceOptions).Assembly, typeof(Hephaestus.Workspaces.WorkspaceRuntimeOptions).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true } && type.GetInterfaces().Contains(typeof(IPullRequests)));

        Assert.Equal([typeof(Hephaestus.GitHub.GitHubGateway)], implementers);
        Assert.Equal(["PublishAsync"], typeof(IPullRequests).GetMethods().Select(method => method.Name));
    }

    [GeneratedRegex(@"""push""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitPush();

    [GeneratedRegex(@"\[""push""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PushCommand();

    [GeneratedRegex(@"""update-ref"", ""-d"", (?<ref>\w+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UpdateRefDelete();
}
