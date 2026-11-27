using Hephaestus.Core.Aliases;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Deployment;

/// <summary>
/// Story 3.1, AD-8: <c>deploy/agents.lock</c> pins one version of each CLI and the digest of every executable, the images carry the lock and
/// no Agent binary, and the volume the host fills is the one the Compose file names.
/// </summary>
public class AgentLockFileTests
{
    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([RepositoryRoot.Path, .. path]));

    private static AgentLock Lock() => AgentLock.Parse(Read("deploy", "agents.lock"));

    [Fact]
    public void The_lock_of_the_release_pins_every_executable_of_every_architecture()
    {
        var agentLock = Lock();

        Assert.Equal(8, agentLock.Binaries.Count);
        Assert.Equal(
            [("claude", 4), ("codex", 2), ("codex-code-mode-host", 2)],
            agentLock.Binaries.GroupBy(binary => binary.File).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => (group.Key, group.Count())));
        Assert.Equal(["claude-code", "codex-cli"], agentLock.Binaries.Select(binary => binary.Agent.Tag()).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Claude_Code_has_a_glibc_and_a_musl_build_and_Codex_CLI_one_static_build_per_architecture()
    {
        var agentLock = Lock();

        foreach (var architecture in Enum.GetValues<AgentArchitecture>())
        {
            var mine = agentLock.For(architecture);
            Assert.Equal([AgentLibc.Glibc, AgentLibc.Musl], mine.Where(b => b.Agent == Agent.ClaudeCode).Select(b => b.Libc).Order());
            Assert.Equal(["codex", "codex-code-mode-host"], mine.Where(b => b.Agent == Agent.CodexCli).Select(b => b.File).Order(StringComparer.Ordinal));
            Assert.All(mine.Where(b => b.Agent == Agent.CodexCli), b => Assert.Equal(AgentLibc.Static, b.Libc));
        }
    }

    [Fact]
    public void Where_the_vendor_publishes_an_archive_the_lock_pins_the_archive_and_the_executable_taken_out_of_it()
    {
        foreach (var binary in Lock().Binaries)
        {
            Assert.Equal(binary.Agent == Agent.CodexCli, binary.IsArchive);
            if (binary.IsArchive)
            {
                Assert.EndsWith(".tar.gz", binary.Url.AbsoluteUri, StringComparison.Ordinal);
                Assert.NotEqual(binary.Sha256, binary.ArchiveSha256);
            }
        }
    }

    [Fact]
    public void Every_download_is_from_the_vendors_official_channel_at_the_pinned_version()
    {
        foreach (var binary in Lock().Binaries)
        {
            var prefix = binary.Agent == Agent.ClaudeCode
                ? $"https://downloads.claude.ai/claude-code-releases/{binary.Version}/"
                : $"https://github.com/openai/codex/releases/download/rust-v{binary.Version}/";
            Assert.StartsWith(prefix, binary.Url.AbsoluteUri, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_image_carries_the_lock_and_never_an_Agent_binary()
    {
        var dockerfile = Read("deploy", "Dockerfile").Split('\n', StringSplitOptions.TrimEntries).Where(line => !line.StartsWith('#')).ToList();

        Assert.Contains("COPY deploy/agents.lock deploy/agents.lock", dockerfile);
        Assert.Contains("!deploy/agents.lock", Read(".dockerignore").Split('\n', StringSplitOptions.TrimEntries));
        Assert.Contains("""Link="agents.lock" CopyToOutputDirectory="PreserveNewest" """.Trim(), Read("src", "Hephaestus.Host", "Hephaestus.Host.csproj"), StringComparison.Ordinal);
        Assert.DoesNotContain(dockerfile, line => new[] { "claude", "codex", "curl", "wget", "downloads.claude.ai", "npm " }.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_lock_is_copied_into_the_build_before_it_publishes()
    {
        var lines = Read("deploy", "Dockerfile").Split('\n', StringSplitOptions.TrimEntries);

        var copy = Array.IndexOf(lines, "COPY deploy/agents.lock deploy/agents.lock");
        var publish = Array.FindIndex(lines, line => line.StartsWith("RUN dotnet publish", StringComparison.Ordinal));
        Assert.True(copy >= 0 && copy < publish, "deploy/agents.lock must be copied before dotnet publish, which places it beside the executable.");
    }

    [Fact]
    public void Only_the_hephaestus_service_mounts_the_agents_volume_and_a_pull_request_that_changes_the_lock_is_checked()
    {
        var workflow = Read(".github", "workflows", "ci.yml");

        Assert.Contains("hephaestus-agents:/var/lib/hephaestus/agents", Read("deploy", "compose.yaml"), StringComparison.Ordinal);
        Assert.Contains("name: Agent CLI pins", workflow, StringComparison.Ordinal);
        Assert.Contains("python3 eng/verify-agents-lock.py", workflow, StringComparison.Ordinal);
        Assert.Contains("bash eng/check-agent-binaries.sh", workflow, StringComparison.Ordinal);
    }
}
