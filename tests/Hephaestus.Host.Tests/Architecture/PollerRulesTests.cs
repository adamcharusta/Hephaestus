using System.Reflection;
using System.Text.RegularExpressions;
using Hephaestus.Core.Ports;
using Hephaestus.GitHub;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>
/// AD-4, Story 2.1: the poller does nothing except append Signals and keep cursors through <c>IPollCursorStore</c>. It decides no
/// trust and changes no Task.
/// </summary>
public partial class PollerRulesTests
{
    private static readonly string[] PollerFiles = ["GitHubPoller.cs", "GitHubInstallations.cs", "GitHubEvents.cs", "GitHubRateLimit.cs"];

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Hephaestus.GitHub", file));

    [Fact]
    public void The_poller_is_given_the_inbox_and_the_cursor_store_and_nothing_that_could_change_a_Task_or_decide_trust()
    {
        var parameters = typeof(GitHubPoller).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Contains(typeof(ISignalInbox), parameters);
        Assert.Contains(typeof(IPollCursorStore), parameters);
        Assert.All(
            parameters,
            type => Assert.Contains(
                type,
                new[] { typeof(IReadStore), typeof(ISecretStore), typeof(ISignalInbox), typeof(IPollCursorStore), typeof(TimeProvider), typeof(GitHubRateLimit), typeof(Func<HttpMessageHandler>), typeof(Func<TimeSpan, CancellationToken, Task>) }));
    }

    [Fact]
    public void The_poller_writes_nothing_but_Signals_and_cursors()
    {
        var source = Source("GitHubPoller.cs");

        Assert.Equal(["AppendAsync"], Members(source, "_inbox"));
        Assert.Equal(["GetAsync", "SetAsync"], Members(source, "_cursors"));
        Assert.Equal(["ReadAsync"], Members(source, "_secrets"));
        Assert.All(Members(source, "_reads"), member => Assert.Contains(member, new[] { "ListProjectsAsync", "GetGitHubAppIdAsync", "ListSettingsAsync" }));
    }

    [Fact]
    public void No_file_of_the_poller_names_a_unit_of_work_trust_a_Task_or_the_configuration_writes()
    {
        foreach (var file in PollerFiles)
        {
            Assert.DoesNotMatch(Forbidden(), Source(file));
        }
    }

    [Theory]
    [InlineData("await unitOfWork.CommitAsync(token);")]
    [InlineData("var trusted = TrustedUser.Is(actor);")]
    [InlineData("private IInstallationConfiguration _configuration;")]
    [InlineData("task.State = TaskState.Queued;")]
    [InlineData("Task<Task> CreateTask()")]
    public void The_rule_that_the_poller_decides_no_trust_and_changes_no_Task_catches_the_names_it_looks_for(string line)
    {
        Assert.Matches(Forbidden(), line);
    }

    [Fact]
    public void The_rule_reads_the_members_the_poller_calls()
    {
        Assert.Equal(["AppendAsync"], Members("await _inbox.AppendAsync(signals, token); _inbox.AppendAsync(x, y);", "_inbox"));
    }

    private static IEnumerable<string> Members(string source, string field) =>
        Regex.Matches(source, $@"\b{field}\.(\w+)").Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal);

    [GeneratedRegex(@"(?i:unitofwork)|IInstallationConfiguration|IChangeFeed|TrustedUser|Trust\.|IsTrusted|\bTaskState\b|\bTask\.State|Task<Task>|class\s+\w*Task\b|ICodeHost")]
    private static partial Regex Forbidden();
}
