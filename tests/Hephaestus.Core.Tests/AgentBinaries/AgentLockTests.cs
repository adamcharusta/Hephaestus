using Hephaestus.Core.Aliases;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;
using Hephaestus.Workspaces.Tests;

namespace Hephaestus.Core.Tests.AgentBinaries;

/// <summary>Story 3.1, AD-8: the lock pins one version of each CLI and the digest of every executable, and a lock that omits one is refused.</summary>
public class AgentLockTests
{
    private static readonly string Valid = new FakeVendor().LockText;

    private static AgentLockException Refused(string text) => Assert.Throws<AgentLockException>(() => AgentLock.Parse(text));

    [Fact]
    public void A_lock_pins_each_executable_per_architecture_and_per_c_library_where_the_vendor_builds_per_c_library()
    {
        var agentLock = AgentLock.Parse(Valid);

        Assert.Equal(
            [
                "claude-code/arm64-glibc/claude",
                "claude-code/arm64-musl/claude",
                "claude-code/x64-glibc/claude",
                "claude-code/x64-musl/claude",
                "codex-cli/arm64-static/codex",
                "codex-cli/arm64-static/codex-code-mode-host",
                "codex-cli/x64-static/codex",
                "codex-cli/x64-static/codex-code-mode-host",
            ],
            agentLock.Binaries.Select(binary => binary.RelativePath).Order(StringComparer.Ordinal));
        Assert.Equal("1.2.3", agentLock.VersionOf(Agent.ClaudeCode));
        Assert.Equal(4, agentLock.For(AgentArchitecture.Arm64).Count);
    }

    [Fact]
    public void An_archive_is_pinned_by_its_own_digest_and_the_digest_of_the_executable_taken_out_of_it()
    {
        var agentLock = AgentLock.Parse(Valid);

        var codex = agentLock.Binaries.Single(binary => binary.RelativePath == "codex-cli/x64-static/codex");
        var claude = agentLock.Binaries.First(binary => binary.Agent == Agent.ClaudeCode);

        Assert.True(codex.IsArchive);
        Assert.NotEqual(codex.Sha256, codex.ArchiveSha256);
        Assert.False(claude.IsArchive);
    }

    [Fact]
    public void Comments_and_blank_lines_and_windows_line_endings_are_ignored()
    {
        var agentLock = AgentLock.Parse("\r\n# nothing here\r\n\r\n" + Valid.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(8, agentLock.Binaries.Count);
    }

    [Fact]
    public void A_lock_that_omits_an_executable_is_refused_naming_it()
    {
        var lines = Valid.Split('\n').Where(line => !line.Contains("codex-code-mode-host", StringComparison.Ordinal) || !line.Contains(" arm64 ", StringComparison.Ordinal));

        var refused = Refused(string.Join('\n', lines));

        Assert.Equal(MessageIds.AgentsLockMissing, refused.Info.Id);
        Assert.Equal("codex-cli/arm64-static/codex-code-mode-host", refused.Info.Arguments[0]);
    }

    [Fact]
    public void An_empty_lock_is_refused()
    {
        Assert.Equal(MessageIds.AgentsLockMissing, Refused("# nothing\n").Info.Id);
    }

    [Fact]
    public void An_agent_has_one_version()
    {
        var refused = Refused(Valid.Replace("codex-cli 1.2.3 x64", "codex-cli 1.2.4 x64", StringComparison.Ordinal));

        Assert.Equal(MessageIds.AgentsLockVersion, refused.Info.Id);
        Assert.Equal(["codex-cli", "1.2.4", "1.2.3"], refused.Info.Arguments[1..]);
    }

    [Fact]
    public void An_executable_pinned_twice_is_refused()
    {
        var first = Valid.Split('\n').First(line => line.StartsWith("claude-code", StringComparison.Ordinal));

        var refused = Refused(Valid + first + "\n");

        Assert.Equal(MessageIds.AgentsLockDuplicate, refused.Info.Id);
    }

    [Theory]
    [InlineData("claude-code 1.2.3 x64 glibc claude", MessageIds.AgentsLockColumns)]
    [InlineData("claude-code 1.2.3 x64 glibc claude 00 https://x.test/a extra extra", MessageIds.AgentsLockColumns)]
    [InlineData("claude 1.2.3 x64 glibc claude {sha} https://x.test/a", MessageIds.AgentsLockValue)]
    [InlineData("claude-code latest x64 glibc claude {sha} https://x.test/a", MessageIds.AgentsLockValue)]
    [InlineData("claude-code 1.2.3 riscv glibc claude {sha} https://x.test/a", MessageIds.AgentsLockValue)]
    [InlineData("claude-code 1.2.3 x64 uclibc claude {sha} https://x.test/a", MessageIds.AgentsLockValue)]
    [InlineData("claude-code 1.2.3 x64 static claude {sha} https://x.test/a", MessageIds.AgentsLockNotAnExecutable)]
    [InlineData("claude-code 1.2.3 x64 glibc ../claude {sha} https://x.test/a", MessageIds.AgentsLockNotAnExecutable)]
    [InlineData("claude-code 1.2.3 x64 glibc claude ABCDEF https://x.test/a", MessageIds.AgentsLockValue)]
    [InlineData("claude-code 1.2.3 x64 glibc claude {sha} http://x.test/a", MessageIds.AgentsLockValue)]
    [InlineData("claude-code 1.2.3 x64 glibc claude {sha} /a/relative/path", MessageIds.AgentsLockValue)]
    [InlineData("claude-code 1.2.3 x64 glibc claude {sha} https://x.test/a nothex", MessageIds.AgentsLockValue)]
    public void A_line_that_is_not_valid_is_refused_with_its_number(string line, string id)
    {
        var sha = new string('a', 64);

        var refused = Refused("# one\n" + line.Replace("{sha}", sha, StringComparison.Ordinal) + "\n");

        Assert.Equal(id, refused.Info.Id);
        Assert.Equal("2", refused.Info.Arguments[0]);
    }

    [Fact]
    public void Every_message_a_lock_can_raise_resolves_in_both_languages()
    {
        foreach (var id in new[] { MessageIds.AgentsLockColumns, MessageIds.AgentsLockValue, MessageIds.AgentsLockNotAnExecutable, MessageIds.AgentsLockVersion, MessageIds.AgentsLockDuplicate, MessageIds.AgentsLockMissing })
        {
            Assert.NotEmpty(MessageCatalog.Instance.Resolve(id, Language.English));
            Assert.NotEmpty(MessageCatalog.Instance.Resolve(id, Language.Polish));
        }
    }

    [Fact]
    public void The_tags_are_the_names_the_lock_and_the_volume_use()
    {
        Assert.Equal("x64", AgentArchitecture.X64.Tag());
        Assert.Equal("arm64", AgentArchitecture.Arm64.Tag());
        Assert.Equal("glibc", AgentLibc.Glibc.Tag());
        Assert.Equal("musl", AgentLibc.Musl.Tag());
        Assert.Equal("static", AgentLibc.Static.Tag());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((AgentArchitecture)99).Tag());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((AgentLibc)99).Tag());
    }
}
