using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;

namespace Hephaestus.Agents.Tests;

/// <summary>Story 3.8, AD-7, AD-10, AD-12: the invocation the adapter describes is the one Story 1.1 proved, the same for both modes, and it carries no secret value.</summary>
public class ClaudeCodeAgentAdapterTests
{
    private readonly IAgentAdapter adapter = new ClaudeCodeAgentAdapter();

    private static InvocationRequest Request(AuthenticationMode mode = AuthenticationMode.ApiKey, Effort? effort = null, string? schema = null, string model = "sonnet") =>
        new(Guid.CreateVersion7(), "instruction.md", mode, model, effort, schema);

    [Fact]
    public void The_invocation_is_claude_print_with_a_stream_JSON_output_and_the_fixed_sentence_and_the_flags_Story_1_1_ran()
    {
        var invocation = adapter.DescribeInvocation(Request());

        Assert.Equal(
            [
                "/hephaestus/agents/claude-code/claude", "-p", "Read /hephaestus/context/instruction.md and carry out the step it describes.",
                "--output-format", "stream-json", "--verbose", "--dangerously-skip-permissions", "--no-session-persistence", "--model", "sonnet",
                "--safe-mode", "--setting-sources", "user", "--strict-mcp-config",
                "--settings", """{"disableAllHooks":true,"disableClaudeAiConnectors":true,"enableAllProjectMcpServers":false}""",
                "--tools", "Bash,Read,Edit,Write",
            ],
            invocation.Command);
        Assert.Equal(AgentPrompt.Sentence, invocation.Command[2]);
        Assert.Equal(ResultLocation.InOutputStream, invocation.Result);
        Assert.Empty(invocation.Files);
    }

    [Fact]
    public void The_argument_vector_is_the_same_for_an_API_key_and_a_subscription_and_only_the_secret_reference_differs()
    {
        var apiKey = adapter.DescribeInvocation(Request(AuthenticationMode.ApiKey));
        var subscription = adapter.DescribeInvocation(Request(AuthenticationMode.Subscription));

        Assert.Equal(apiKey.Command, subscription.Command);
        Assert.Equal(apiKey.Environment, subscription.Environment);
        Assert.Equal(apiKey.EgressHosts, subscription.EgressHosts);
        Assert.Equal([new SecretReference("anthropic-api-key", "ANTHROPIC_API_KEY")], apiKey.SecretReferences);
        Assert.Equal([new SecretReference("claude-code-oauth-token", "CLAUDE_CODE_OAUTH_TOKEN")], subscription.SecretReferences);
        Assert.All(new[] { apiKey, subscription }, invocation => Assert.True(Assert.Single(invocation.SecretReferences).IsEnvironmentVariable));
    }

    [Fact]
    public void The_environment_switches_off_self_update_account_features_and_nonessential_traffic_and_holds_no_secret()
    {
        var environment = adapter.DescribeInvocation(Request()).Environment;

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["CLAUDE_CODE_DISABLE_AGENT_VIEW"] = "1",
                ["CLAUDE_CODE_DISABLE_ARTIFACT"] = "1",
                ["CLAUDE_CODE_DISABLE_CRON"] = "1",
                ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
                ["CLAUDE_CODE_DISABLE_OFFICIAL_MARKETPLACE_AUTOINSTALL"] = "1",
                ["CLAUDE_CODE_ENABLE_CFC"] = "0",
                ["DISABLE_AUTOUPDATER"] = "1",
                ["DISABLE_UPDATES"] = "1",
                ["ENABLE_CLAUDEAI_MCP_SERVERS"] = "false",
            },
            environment);
        Assert.DoesNotContain("ANTHROPIC_API_KEY", environment.Keys);
        Assert.DoesNotContain("CLAUDE_CODE_OAUTH_TOKEN", environment.Keys);
        Assert.DoesNotContain("HOME", environment.Keys);
    }

    [Fact]
    public void The_CLI_takes_settings_only_from_the_throwaway_home_offers_a_closed_list_of_tools_and_reads_no_repository_instructions()
    {
        var command = adapter.DescribeInvocation(Request()).Command;

        Assert.Equal("user", command[command.ToList().IndexOf("--setting-sources") + 1]);
        Assert.Contains("--safe-mode", command);
        Assert.Contains("--strict-mcp-config", command);
        var tools = command[command.ToList().IndexOf("--tools") + 1].Split(',');
        Assert.Equal(["Bash", "Read", "Edit", "Write"], tools);
        Assert.DoesNotContain("Skill", tools);
        Assert.DoesNotContain("Task", tools);
        Assert.Equal("--tools", command[^2]);
        foreach (var forbidden in new[] { "--bare", "--resume", "-r", "--continue", "--add-dir", "--plugin-dir", "--plugin-url", "--mcp-config", "--allowedTools", "--agents", "--system-prompt" })
        {
            Assert.DoesNotContain(forbidden, command);
        }
    }

    [Fact]
    public void The_executable_is_where_the_runtime_mounts_the_build_that_matches_the_image()
    {
        Assert.Equal("/hephaestus/agents/claude-code/claude", ClaudeCodeAgentAdapter.Executable);
        Assert.Equal(ClaudeCodeAgentAdapter.Executable, adapter.DescribeInvocation(Request()).Command[0]);
    }

    [Theory]
    [InlineData(AuthenticationMode.ApiKey)]
    [InlineData(AuthenticationMode.Subscription)]
    public void The_hosts_are_the_ones_recorded_for_the_pinned_version(AuthenticationMode mode)
    {
        Assert.Equal(["api.anthropic.com"], adapter.DescribeInvocation(Request(mode)).EgressHosts);
        Assert.Equal(["api.anthropic.com"], adapter.DescribeProbe(mode).EgressHosts);
    }

    [Theory]
    [InlineData(Effort.Low, "low")]
    [InlineData(Effort.Medium, "medium")]
    [InlineData(Effort.High, "high")]
    [InlineData(Effort.ExtraHigh, "xhigh")]
    [InlineData(Effort.Max, "max")]
    public void Every_Effort_of_Core_is_the_effort_flag_of_the_CLI(Effort effort, string flag)
    {
        Assert.True(adapter.SupportsEffort(effort).IsSupported);

        var command = adapter.DescribeInvocation(Request(effort: effort)).Command;

        Assert.Equal(flag, command[command.ToList().IndexOf("--effort") + 1]);
    }

    [Fact]
    public void No_Effort_leaves_the_CLI_its_own_and_adds_no_flag()
    {
        Assert.DoesNotContain("--effort", adapter.DescribeInvocation(Request()).Command);
    }

    [Fact]
    public void An_Effort_the_CLI_has_no_value_for_is_declared_unsupported_and_refused_never_replaced()
    {
        var unknown = (Effort)99;

        var support = adapter.SupportsEffort(unknown);

        Assert.False(support.IsSupported);
        Assert.Equal(MessageIds.AgentEffortUnsupported, support.Reason!.Value.Id);
        Assert.Equal(["claude-code", "99"], support.Reason.Value.Arguments);
        var refusal = Assert.Throws<UnsupportedInvocationException>(() => adapter.DescribeInvocation(Request(effort: unknown)));
        Assert.Equal(MessageIds.AgentEffortUnsupported, refusal.Reason.Id);
    }

    [Fact]
    public void A_mode_the_hardening_cannot_be_guaranteed_in_is_declared_unsupported_and_refused()
    {
        var unknown = (AuthenticationMode)99;

        Assert.True(adapter.SupportsAuthentication(AuthenticationMode.ApiKey).IsSupported);
        Assert.True(adapter.SupportsAuthentication(AuthenticationMode.Subscription).IsSupported);
        Assert.Null(adapter.SupportsAuthentication(AuthenticationMode.ApiKey).Reason);
        var support = adapter.SupportsAuthentication(unknown);
        Assert.False(support.IsSupported);
        Assert.Equal(MessageIds.AgentAuthenticationUnsupported, support.Reason!.Value.Id);
        Assert.Equal(["claude-code", "99"], support.Reason.Value.Arguments);
        Assert.Throws<UnsupportedInvocationException>(() => adapter.DescribeInvocation(Request(unknown)));
        Assert.Throws<UnsupportedInvocationException>(() => adapter.DescribeProbe(unknown));
    }

    [Fact]
    public void A_result_schema_is_given_to_the_CLI_as_one_line_of_JSON()
    {
        var command = adapter.DescribeInvocation(Request(schema: "{\n  \"type\": \"object\",\n  \"required\": [ \"outcome\" ]\n}")).Command;

        Assert.Equal("""{"type":"object","required":["outcome"]}""", command[command.ToList().IndexOf("--json-schema") + 1]);
        Assert.Equal("--tools", command[^2]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("\"text\"")]
    public void A_result_schema_that_is_not_a_JSON_object_is_refused(string schema)
    {
        Assert.Throws<ArgumentException>(() => adapter.DescribeInvocation(Request(schema: schema)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--dangerously-skip-permissions")]
    [InlineData("a b")]
    public void A_model_that_could_be_taken_for_an_option_or_is_not_a_name_is_refused(string model)
    {
        Assert.Throws<ArgumentException>(() => adapter.DescribeInvocation(Request(model: model)));
    }

    [Fact]
    public void The_instruction_must_be_placed_where_the_prompt_points()
    {
        Assert.Throws<ArgumentException>(() => adapter.DescribeInvocation(new InvocationRequest(Guid.CreateVersion7(), "other.md", AuthenticationMode.ApiKey, "sonnet")));
    }

    [Fact]
    public void The_environment_of_an_invocation_cannot_be_changed_by_whoever_holds_it()
    {
        var environment = adapter.DescribeInvocation(Request()).Environment;

        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)environment)["HOME"] = "/");
        Assert.DoesNotContain("HOME", adapter.DescribeInvocation(Request()).Environment.Keys);
    }

    [Fact]
    public void A_request_is_required()
    {
        Assert.Throws<ArgumentNullException>(() => adapter.DescribeInvocation(null!));
        Assert.Throws<ArgumentNullException>(() => adapter.ClassifyExit(0, null!));
        Assert.Throws<ArgumentNullException>(() => adapter.Parse(null!));
    }

    [Theory]
    [InlineData(AuthenticationMode.ApiKey, "anthropic-api-key")]
    [InlineData(AuthenticationMode.Subscription, "claude-code-oauth-token")]
    public void The_probe_is_a_one_word_request_under_the_same_hardening_with_the_secret_of_the_mode(AuthenticationMode mode, string secret)
    {
        var probe = adapter.DescribeProbe(mode);

        Assert.Equal(
            [
                "/hephaestus/agents/claude-code/claude", "-p", "Reply with exactly one word: pong",
                "--output-format", "stream-json", "--verbose", "--dangerously-skip-permissions", "--no-session-persistence",
                "--safe-mode", "--setting-sources", "user", "--strict-mcp-config",
                "--settings", ClaudeCodeAgentAdapter.Settings,
                "--tools", "Bash,Read,Edit,Write",
            ],
            probe.Command);
        Assert.Equal(secret, Assert.Single(probe.SecretReferences).Name);
        Assert.Equal(adapter.DescribeInvocation(Request(mode)).Environment, probe.Environment);
        Assert.Equal(ResultLocation.InOutputStream, probe.Result);
    }

    [Fact]
    public void The_adapter_is_the_one_of_Claude_Code_and_runs_only_against_the_version_the_lock_pins()
    {
        var pinned = AgentLock.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "deploy", "agents.lock")));

        Assert.Equal(Agent.ClaudeCode, adapter.Agent);
        Assert.Equal(pinned.VersionOf(Agent.ClaudeCode), adapter.PinnedVersion);
        Assert.Equal(ClaudeCodeAgentAdapter.Version, adapter.PinnedVersion);
    }
}
