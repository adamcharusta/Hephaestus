using Hephaestus.Core.Failures;
using Hephaestus.Core.Invocations;

namespace Hephaestus.Agents.Tests;

/// <summary>Story 3.8: a stored output stream is parsed into normalized events and a result, and an exit is classified only when the stream names the failure (AD-10, AD-11). The streams are recorded from the pinned CLI.</summary>
public class ClaudeCodeStreamTests
{
    private readonly ClaudeCodeAgentAdapter adapter = new();

    [Fact]
    public void The_transcripts_are_the_stream_of_the_version_the_adapter_was_released_with()
    {
        Assert.Equal(ClaudeCodeAgentAdapter.Version, Transcript.Version);
        foreach (var run in new[] { "api-key-tool-run", "subscription-tool-run", "api-key-schema", "wrong-key", "no-credential" })
        {
            Assert.Contains($"\"claude_code_version\":\"{Transcript.Version}\"", string.Join('\n', Transcript.Lines(run)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_run_that_called_a_tool_yields_its_events_in_order_and_its_result_with_usage()
    {
        var output = adapter.Parse(Transcript.Lines("api-key-tool-run"));

        Assert.Equal(
            [AgentEventKind.Started, AgentEventKind.ToolCall, AgentEventKind.ToolResult, AgentEventKind.Message],
            output.Events.Select(e => e.Kind));
        Assert.Equal("claude-haiku-5-5", output.Events[0].Text);
        var call = output.Events[1];
        Assert.Equal("Bash", call.Name);
        Assert.Contains("id -u; echo tool-$((6*7))", call.Text, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 21, 26, 56, 979, TimeSpan.Zero), call.At);
        var answer = output.Events[2];
        Assert.Equal("Bash", answer.Name);
        Assert.Equal("10001\ntool-42", answer.Text);
        Assert.False(answer.IsError);
        Assert.Equal("pong", output.Events[3].Text);

        var result = Assert.IsType<AgentResult>(output.Result);
        Assert.False(result.IsError);
        Assert.Equal("pong", result.Text);
        Assert.Null(result.StructuredOutput);
        Assert.Null(result.Error);
        Assert.Equal("claude-haiku-5-5", result.Model);
        Assert.Equal(new AgentUsage(4, 97, 9568, 2409, 0.00044570499999999996m), result.Usage);
        Assert.Empty(output.RateLimits);
        Assert.Equal(0, output.UnreadableLines);
    }

    [Fact]
    public void A_subscription_run_keeps_the_limit_report_the_CLI_made_and_decides_nothing_from_it()
    {
        var output = adapter.Parse(Transcript.Lines("subscription-tool-run"));

        var limit = Assert.Single(output.RateLimits);
        Assert.Equal(new AgentRateLimit("seven_day", "allowed", DateTimeOffset.FromUnixTimeSeconds(1796162400)), limit);
        var @event = Assert.Single(output.Events, e => e.Kind == AgentEventKind.RateLimit);
        Assert.Equal("seven_day", @event.Name);
        Assert.Equal("allowed", @event.Text);
        Assert.Null(adapter.ClassifyExit(0, output));
    }

    [Fact]
    public void A_run_with_a_result_schema_leaves_the_document_in_the_structured_output_of_the_result()
    {
        var output = adapter.Parse(Transcript.Lines("api-key-schema"));

        var result = Assert.IsType<AgentResult>(output.Result);
        Assert.Equal("""{"answer":"Warsaw is the capital of Poland.","confidence":99}""", result.StructuredOutput);
        Assert.Equal("StructuredOutput", output.Events.Where(e => e.Kind == AgentEventKind.ToolCall).Select(e => e.Name).Single());
        Assert.Null(adapter.ClassifyExit(0, output));
    }

    [Fact]
    public void A_run_that_ended_well_is_no_failure_whatever_the_stream_held()
    {
        var output = adapter.Parse(Transcript.Lines("api-key-tool-run"));

        Assert.Null(adapter.ClassifyExit(0, output));
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("no-credential")]
    public void A_credential_the_CLI_says_it_cannot_use_is_an_agent_error(string run)
    {
        var output = adapter.Parse(Transcript.Lines(run));

        var result = Assert.IsType<AgentResult>(output.Result);
        Assert.True(result.IsError);
        Assert.Equal("authentication_failed", result.Error);
        Assert.Equal(FailureClass.AgentError, adapter.ClassifyExit(1, output));
        Assert.Contains(output.Events, e => e is { Kind: AgentEventKind.Message, IsError: true });
    }

    [Fact]
    public void A_wrong_key_reports_the_usage_it_reported_which_was_zero_and_a_stream_with_none_reports_null()
    {
        var wrongKey = adapter.Parse(Transcript.Lines("wrong-key"));

        Assert.Equal(new AgentUsage(0, 0, 0, 0, 0m), wrongKey.Result!.Usage);
        Assert.Null(adapter.Parse(["""{"type":"result","subtype":"success","is_error":false,"result":"done"}"""]).Result!.Usage);
    }

    [Fact]
    public void A_field_the_CLI_did_not_report_is_null_and_never_zero()
    {
        var result = adapter.Parse(["""{"type":"result","subtype":"success","is_error":false,"result":"x","usage":{"output_tokens":7}}"""]).Result!;

        Assert.Equal(new AgentUsage(null, 7, null, null, null), result.Usage);
    }

    [Fact]
    public void A_result_the_CLI_ended_in_an_error_of_its_own_is_an_agent_error()
    {
        var output = adapter.Parse(["""{"type":"result","subtype":"error_max_turns","is_error":true,"result":"stopped"}"""]);

        Assert.Equal("error_max_turns", output.Result!.Error);
        Assert.Equal(FailureClass.AgentError, adapter.ClassifyExit(1, output));
    }

    [Fact]
    public void A_result_that_names_no_error_is_not_classified_by_guessing_even_when_it_is_an_error()
    {
        var output = adapter.Parse(["""{"type":"result","subtype":"success","is_error":true,"api_error_status":429,"result":"Usage limit reached"}"""]);

        Assert.True(output.Result!.IsError);
        Assert.Null(output.Result.Error);
        Assert.Equal(FailureClass.Unknown, adapter.ClassifyExit(1, output));
    }

    [Theory]
    [InlineData("rate_limit")]
    [InlineData("billing_error")]
    [InlineData("server_error")]
    [InlineData("invalid_request")]
    public void An_error_the_CLI_names_that_is_not_one_of_the_two_it_is_known_by_is_kept_and_not_classified(string error)
    {
        var output = adapter.Parse(
        [
            $$$"""{"type":"assistant","error":"{{{error}}}","message":{"content":[{"type":"text","text":"Limit reached"}]}}""",
            """{"type":"result","subtype":"success","is_error":true,"result":"Limit reached"}""",
        ]);

        Assert.Equal(error, output.Result!.Error);
        Assert.Equal(FailureClass.Unknown, adapter.ClassifyExit(1, output));
    }

    [Fact]
    public void An_error_of_an_earlier_message_is_not_the_cause_of_a_later_result_that_names_none()
    {
        var output = adapter.Parse(
        [
            """{"type":"assistant","error":"authentication_failed","message":{"content":[{"type":"text","text":"Invalid API key"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Continuing"}]}}""",
            """{"type":"result","subtype":"success","is_error":true,"result":"Stopped"}""",
        ]);

        Assert.Null(output.Result!.Error);
        Assert.Equal(FailureClass.Unknown, adapter.ClassifyExit(1, output));
    }

    [Fact]
    public void A_result_that_is_not_an_error_has_no_error_whatever_a_message_said()
    {
        var output = adapter.Parse(
        [
            """{"type":"assistant","error":"rate_limit","message":{"content":[{"type":"text","text":"Retrying"}]}}""",
            """{"type":"result","subtype":"success","is_error":false,"result":"done"}""",
        ]);

        Assert.Null(output.Result!.Error);
        Assert.Null(adapter.ClassifyExit(0, output));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(137)]
    public void A_stream_with_no_result_is_unknown_whatever_the_exit_code(int exitCode)
    {
        var output = adapter.Parse(Transcript.Lines("api-key-tool-run").Take(3));

        Assert.Null(output.Result);
        Assert.Equal(FailureClass.Unknown, adapter.ClassifyExit(exitCode, output));
    }

    [Fact]
    public void A_non_zero_exit_after_a_result_that_ended_well_is_unknown()
    {
        var output = adapter.Parse(Transcript.Lines("api-key-tool-run"));

        Assert.Equal(FailureClass.Unknown, adapter.ClassifyExit(2, output));
    }

    [Fact]
    public void Lines_that_are_not_events_are_counted_and_skipped_and_never_stop_the_parse()
    {
        var lines = new[] { "", "   ", "not json", "{\"truncated\":", "[1,2]", "{\"no\":\"type\"}", "{\"type\":\"unknown-kind\"}", "{\"type\":\"system\",\"subtype\":\"ui_invalidate\"}", """{"type":"result","subtype":"success","is_error":false,"result":"ok"}""" };

        var output = adapter.Parse(lines);

        Assert.Equal(4, output.UnreadableLines);
        Assert.Equal("ok", output.Result!.Text);
        Assert.Empty(output.Events);
    }

    [Fact]
    public void The_last_result_wins_and_a_model_comes_from_the_usage_when_the_stream_never_started()
    {
        var output = adapter.Parse(
        [
            """{"type":"result","subtype":"success","is_error":false,"result":"first","modelUsage":{"claude-x":{}}}""",
            """{"type":"result","subtype":"success","is_error":false,"result":"second","modelUsage":{"claude-y":{}}}""",
        ]);

        Assert.Equal("second", output.Result!.Text);
        Assert.Equal("claude-y", output.Result.Model);
    }

    [Fact]
    public void A_tool_result_with_parts_is_joined_and_an_unknown_call_has_no_name()
    {
        var output = adapter.Parse(
        [
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"/workspace/a"}},{"type":"thinking","thinking":"x"},{"type":"text"}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","is_error":true,"content":[{"type":"text","text":"one"},{"type":"image"},{"type":"text","text":"two"}]},{"type":"tool_result","tool_use_id":"other","content":42},{"type":"text","text":"ignored"}]}}""",
            """{"type":"user","message":"not a message object"}""",
            """{"type":"assistant"}""",
        ]);

        Assert.Equal([AgentEventKind.ToolCall, AgentEventKind.Message, AgentEventKind.ToolResult, AgentEventKind.ToolResult], output.Events.Select(e => e.Kind));
        Assert.Equal("{\"file_path\":\"/workspace/a\"}", output.Events[0].Text);
        var joined = output.Events[2];
        Assert.Equal(("Read", "one\ntwo", true), (joined.Name, joined.Text, joined.IsError));
        var unknown = output.Events[3];
        Assert.Equal((null, null, false), (unknown.Name, unknown.Text, unknown.IsError));
    }

    [Fact]
    public void The_text_of_an_event_is_cut_at_the_bound()
    {
        var oversized = new string('x', AgentEvent.MaximumTextLength + 500);

        var output = adapter.Parse([$$$"""{"type":"assistant","message":{"content":[{"type":"text","text":"{{{oversized}}}"}]}}"""]);

        Assert.Equal(AgentEvent.MaximumTextLength, Assert.Single(output.Events).Text!.Length);
    }

    [Fact]
    public void The_cut_never_ends_on_half_of_a_surrogate_pair()
    {
        var text = new string('x', AgentEvent.MaximumTextLength - 1) + "😀tail";

        var output = adapter.Parse([$$$"""{"type":"assistant","message":{"content":[{"type":"text","text":"{{{text}}}"}]}}"""]);

        Assert.Equal(AgentEvent.MaximumTextLength - 1, Assert.Single(output.Events).Text!.Length);
    }

    [Fact]
    public void A_line_with_a_lone_surrogate_is_unreadable_and_not_an_exception()
    {
        var output = adapter.Parse(["{\"type\":\"result\",\"result\":\"" + '\ud800' + "\"}", """{"type":"result","subtype":"success","is_error":false,"result":"ok"}"""]);

        Assert.Equal(1, output.UnreadableLines);
        Assert.Equal("ok", output.Result!.Text);
    }

    [Fact]
    public void A_limit_report_that_lacks_a_field_is_kept_with_what_it_has()
    {
        var output = adapter.Parse(["""{"type":"rate_limit_event","rate_limit_info":{"resetsAt":-5}}""", """{"type":"rate_limit_event"}"""]);

        Assert.Equal(new AgentRateLimit("unknown", "unknown", null), Assert.Single(output.RateLimits));
    }
}
