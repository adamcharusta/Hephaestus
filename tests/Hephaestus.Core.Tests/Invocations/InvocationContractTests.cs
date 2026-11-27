using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Tests.Invocations;

/// <summary>AD-10, AD-12: the parts of the contract of an invocation that every adapter shares.</summary>
public class InvocationContractTests
{
    [Fact]
    public void The_prompt_is_one_sentence_that_points_at_the_instruction_file_of_the_context_directory()
    {
        Assert.Equal("/hephaestus/context/instruction.md", AgentPrompt.InstructionPath);
        Assert.Contains(AgentPrompt.InstructionPath, AgentPrompt.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', AgentPrompt.Sentence);
        Assert.EndsWith(".", AgentPrompt.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_result_is_in_the_output_stream_or_in_a_file()
    {
        Assert.Equal(ResultSource.OutputStream, ResultLocation.InOutputStream.Source);
        Assert.Null(ResultLocation.InOutputStream.Path);
        var file = ResultLocation.InFile("/workspace/result.json");
        Assert.Equal((ResultSource.File, "/workspace/result.json"), (file.Source, file.Path));
        Assert.Throws<ArgumentException>(() => ResultLocation.InFile(""));
        Assert.Throws<ArgumentException>(() => ResultLocation.InFile("result.json"));
    }

    [Fact]
    public void A_supported_thing_has_no_reason_and_an_unsupported_one_has_one()
    {
        Assert.True(Support.Yes.IsSupported);
        Assert.Null(Support.Yes.Reason);
        var no = Support.No(new Message(MessageIds.AgentEffortUnsupported, "codex-cli", "max"));
        Assert.False(no.IsSupported);
        Assert.Equal(MessageIds.AgentEffortUnsupported, no.Reason!.Value.Id);
        Assert.Equal(MessageIds.AgentEffortUnsupported, new UnsupportedInvocationException(no.Reason.Value).Message);
    }

    [Fact]
    public void The_messages_of_unsupported_modes_and_Efforts_read_in_both_languages()
    {
        foreach (var language in Enum.GetValues<Language>())
        {
            var mode = MessageCatalog.Instance.Resolve(new Message(MessageIds.AgentAuthenticationUnsupported, "codex-cli", "subscription"), language);
            var effort = MessageCatalog.Instance.Resolve(new Message(MessageIds.AgentEffortUnsupported, "codex-cli", "max"), language);

            Assert.Contains("codex-cli", mode, StringComparison.Ordinal);
            Assert.Contains("subscription", mode, StringComparison.Ordinal);
            Assert.Contains("max", effort, StringComparison.Ordinal);
        }
    }
}
