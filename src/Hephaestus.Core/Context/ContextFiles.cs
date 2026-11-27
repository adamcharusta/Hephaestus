using Hephaestus.Core.Invocations;

namespace Hephaestus.Core.Context;

/// <summary>The fixed names of the context files of a step run (AD-12), part of the versioned surface of an installation: a Methodology's template refers to them by path.</summary>
public static class ContextFiles
{
    public const string Instruction = AgentPrompt.InstructionFile;
    public const string Task = "task.json";
    public const string Issue = "issue.md";
    public const string Comments = "comments.json";
    public const string Plan = "plan.md";
    public const string Findings = "findings.json";
    public const string Validation = "validation.json";
    public const string Handoff = "handoff.md";
    public const string ResultSchema = "result.schema.json";

    /// <summary>Every name, in the order the architecture lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Instruction, Task, Issue, Comments, Plan, Findings, Validation, Handoff, ResultSchema];
}
