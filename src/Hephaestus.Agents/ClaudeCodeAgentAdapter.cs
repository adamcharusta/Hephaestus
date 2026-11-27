using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Agents;

/// <summary>
/// The adapter of Claude Code (AD-10, Story 3.8). It describes <c>claude -p</c> with a stream-JSON output exactly as Story 1.1 ran it, parses what it printed, and
/// starts nothing: the runtime starts the container and knows nothing of Claude Code. It is released with one version of the CLI, <see cref="Version"/>, which
/// <c>deploy/agents.lock</c> pins, and a change of either repeats the credentialed runs of <c>docs/spikes/injection</c> (AD-7, AD-8).
/// </summary>
public sealed class ClaudeCodeAgentAdapter : IAgentAdapter
{
    /// <summary>The version of the CLI this adapter was released with.</summary>
    public const string Version = "2.1.332";

    /// <summary>The executable in the step container, the same whichever C library the Project's image has (AD-8).</summary>
    public const string Executable = StepContainerLayout.Agents + "/claude-code/claude";

    /// <summary>The one host the CLI needs, in both modes, at <see cref="Version"/> (the injection spike, <c>04-proxy</c>).</summary>
    public const string EgressHost = "api.anthropic.com";

    /// <summary>The secret that holds the API key, and the variable the CLI reads it from.</summary>
    public const string ApiKeySecret = "anthropic-api-key";

    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    /// <summary>The secret that holds the token <c>claude setup-token</c> prints, and the variable the CLI reads it from.</summary>
    public const string SubscriptionSecret = "claude-code-oauth-token";

    public const string SubscriptionVariable = "CLAUDE_CODE_OAUTH_TOKEN";

    /// <summary>What a probe asks: a request the CLI cannot answer without a credential that works and capacity to spend.</summary>
    public const string ProbeSentence = "Reply with exactly one word: pong";

    /// <summary>The tools an invocation offers, a closed list: it holds no <c>Skill</c> and no <c>Task</c> tool, nothing that reaches outside the container, and nothing a repository can add to (AD-7, AD-12).</summary>
    public const string Tools = "Bash,Read,Edit,Write";

    /// <summary>The settings that switch off what a repository's settings could otherwise run or the vendor account could add; read after <c>--setting-sources user</c>, which leaves the repository's out.</summary>
    public const string Settings = """{"disableAllHooks":true,"disableClaudeAiConnectors":true,"enableAllProjectMcpServers":false}""";

    private static readonly FrozenDictionary<string, string> Switches = new Dictionary<string, string>
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
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public Agent Agent => Agent.ClaudeCode;

    public string PinnedVersion => Version;

    public Support SupportsAuthentication(AuthenticationMode mode) =>
        Enum.IsDefined(mode) ? Support.Yes : Support.No(new Message(MessageIds.AgentAuthenticationUnsupported, Agent.Tag(), ((int)mode).ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public Support SupportsEffort(Effort effort) =>
        Enum.IsDefined(effort) ? Support.Yes : Support.No(new Message(MessageIds.AgentEffortUnsupported, Agent.Tag(), ((int)effort).ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public AgentInvocation DescribeInvocation(InvocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AliasShape.IsValidModel(request.Model))
        {
            throw new ArgumentException("The Model is not a model name.", nameof(request));
        }

        if (request.InstructionPath != AgentPrompt.InstructionFile)
        {
            throw new ArgumentException($"The prompt points at {AgentPrompt.InstructionPath}; the instruction must be placed there.", nameof(request));
        }

        Require(SupportsAuthentication(request.Authentication));
        var command = new List<string> { Executable, "-p", AgentPrompt.Sentence };
        command.AddRange(["--output-format", "stream-json", "--verbose", "--dangerously-skip-permissions", "--no-session-persistence", "--model", request.Model]);
        if (request.Effort is { } effort)
        {
            Require(SupportsEffort(effort));
            command.AddRange(["--effort", effort.Tag()]);
        }

        if (request.ResultSchema is not null)
        {
            command.AddRange(["--json-schema", Compact(request.ResultSchema)]);
        }

        return Describe(command, request.Authentication);
    }

    public AgentInvocation DescribeProbe(AuthenticationMode mode)
    {
        Require(SupportsAuthentication(mode));
        return Describe([Executable, "-p", ProbeSentence, "--output-format", "stream-json", "--verbose", "--dangerously-skip-permissions", "--no-session-persistence"], mode);
    }

    public AgentOutput Parse(IEnumerable<string> lines) => ClaudeCodeStream.Parse(lines);

    public FailureClass? ClassifyExit(int exitCode, AgentOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var result = output.Result;
        if (exitCode == 0 && result is { IsError: false })
        {
            return null;
        }

        return result is { IsError: true, Error: { } error } && (error == ClaudeCodeStream.AuthenticationFailed || error.StartsWith(ClaudeCodeStream.ErrorSubtypePrefix, StringComparison.Ordinal))
            ? FailureClass.AgentError
            : FailureClass.Unknown;
    }

    /// <summary>The hardening is the same in every invocation, the probe included: it comes last, so that the variadic list of tools ends the vector.</summary>
    private static AgentInvocation Describe(List<string> command, AuthenticationMode mode)
    {
        command.AddRange(["--safe-mode", "--setting-sources", "user", "--strict-mcp-config", "--settings", Settings, "--tools", Tools]);
        var reference = mode == AuthenticationMode.ApiKey
            ? new SecretReference(ApiKeySecret, ApiKeyVariable)
            : new SecretReference(SubscriptionSecret, SubscriptionVariable);
        return new AgentInvocation(command, Switches, [reference], [], [EgressHost], ResultLocation.InOutputStream);
    }

    private static void Require(Support support)
    {
        if (!support.IsSupported)
        {
            throw new UnsupportedInvocationException(support.Reason!.Value);
        }
    }

    /// <summary>The schema Core generated, as one line; the CLI takes it as an argument and reads it as JSON.</summary>
    private static string Compact(string schema)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(schema);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The result schema is not JSON.", nameof(schema), exception);
        }

        return node is JsonObject ? node.ToJsonString() : throw new ArgumentException("The result schema is not a JSON object.", nameof(schema));
    }
}
