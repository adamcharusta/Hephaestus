using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Invocations;

/// <summary>
/// The prompt of every Agent invocation (AD-12): one fixed sentence that points at the instruction file Core wrote for the step run, so that
/// no adapter assembles a prompt of its own and a Handoff changes who does the work and not what is asked.
/// </summary>
public static class AgentPrompt
{
    /// <summary>The name of the instruction file in the context directory.</summary>
    public const string InstructionFile = "instruction.md";

    /// <summary>Where the instruction is in the step container.</summary>
    public const string InstructionPath = StepContainerLayout.Context + "/" + InstructionFile;

    public const string Sentence = "Read " + InstructionPath + " and carry out the step it describes.";
}
