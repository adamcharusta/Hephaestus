using System.Text.RegularExpressions;
using Hephaestus.Core.GitHub;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>AD-4, Story 1.7: <c>Hephaestus.GitHub</c> is the only project that references Octokit or reads the GitHub App credential.</summary>
public partial class GitHubRulesTests
{
    private const string Gateway = "Hephaestus.GitHub";

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string file) => Path.GetRelativePath(RepositoryRoot.Path, file).Replace('\\', '/');

    private static bool IsInGateway(string file) => Relative(file).StartsWith($"src/{Gateway}/", StringComparison.Ordinal);

    [Fact]
    public void Only_the_gateway_names_Octokit_as_a_package()
    {
        var projects = ProjectGraphReader.ReadSourceProjects(RepositoryRoot.Path);

        var namingIt = projects.Where(p => p.DirectPackages.Any(package => package.StartsWith("Octokit", StringComparison.OrdinalIgnoreCase))).Select(p => p.Name);

        Assert.Equal([Gateway], namingIt);
    }

    [Fact]
    public void No_source_file_outside_the_gateway_uses_Octokit()
    {
        var offenders = SourceFiles().Where(file => !IsInGateway(file)).Where(file => OctokitUse().IsMatch(File.ReadAllText(file))).Select(Relative);

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_credential_is_named_only_in_the_gateway_and_in_the_two_files_of_Core_that_define_and_describe_it()
    {
        var allowed = new[] { "src/Hephaestus.Core/GitHub/GitHubAppCredential.cs", "src/Hephaestus.Core/Ports/ICodeHost.cs" };

        var offenders = SourceFiles()
            .Where(file => !IsInGateway(file) && !allowed.Contains(Relative(file)))
            .Where(file => CredentialNames().IsMatch(File.ReadAllText(file)))
            .Select(Relative);

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_host_never_reads_a_secret_back()
    {
        var readers = SourceFiles()
            .Where(file => Relative(file).StartsWith("src/Hephaestus.Host/", StringComparison.Ordinal))
            .Where(file => SecretReads().IsMatch(File.ReadAllText(file)))
            .Select(Relative);

        Assert.Empty(readers);
    }

    [Theory]
    [InlineData("await _secrets.ReadAsync(name, token);")]
    [InlineData("var all = await secretStore.ReadAllAsync(token);")]
    [InlineData("return this.Store.ReadAsync(\"x\", ct);")]
    public void The_rule_that_the_host_never_reads_a_secret_back_catches_any_member_name(string line)
    {
        Assert.Matches(SecretReads(), line);
    }

    [Fact]
    public void The_gateway_does_read_the_credential_under_the_name_the_rule_looks_for()
    {
        Assert.Equal("github-app-private-key", GitHubAppCredential.PrivateKeySecretName);
        Assert.Matches(CredentialNames(), GitHubAppCredential.PrivateKeySecretName);
        Assert.Contains(SourceFiles().Where(IsInGateway), file => File.ReadAllText(file).Contains("GitHubAppCredential.PrivateKeySecretName", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\busing\s+Octokit|\bOctokit\.[A-Z]")]
    private static partial Regex OctokitUse();

    [GeneratedRegex(@"GitHubAppCredential|PrivateKeySecretName|github-app-private-key")]
    private static partial Regex CredentialNames();

    // Any member, whatever it is called: a read of one value or of every value.
    [GeneratedRegex(@"\.Read(All)?Async\(")]
    private static partial Regex SecretReads();
}
