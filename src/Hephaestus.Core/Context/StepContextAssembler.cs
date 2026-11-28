using System.Text;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Methodologies;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Results;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Context;

/// <summary>
/// Assembles the one context file set of a step run (AD-12, FR-62). <c>instruction.md</c> is the bytes of the Methodology's template for the step kind and nothing is added to
/// them, so the instructions do not depend on the Agent, the language, or the Task; everything particular to the Task is in the other files. The files are written with no time,
/// no Agent, and no ordering that depends on anything but the input, so assembling the same step twice gives the same bytes.
/// </summary>
public static class StepContextAssembler
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <exception cref="MethodologyException">The Methodology is not shipped, or has no template for the step kind.</exception>
    public static async Task<AssembledContext> AssembleAsync(IMethodologyCatalog catalog, StepContextInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(input);

        var name = string.IsNullOrWhiteSpace(input.MethodologyName) ? ProjectConfigurationFile.DefaultMethodology : input.MethodologyName;
        var methodology = await catalog.ReadAsync(name, cancellationToken)
            ?? throw new MethodologyException(new Message(MessageIds.MethodologyUnavailable, name));
        var template = MethodologyTemplates.Select(methodology, input.Kind)
            ?? throw new MethodologyException(new Message(MessageIds.MethodologyTemplateMissing, name, input.Kind.Tag()));
        var schema = ResultSchemas.For(input.Kind);

        var files = new List<WorkspaceFile>
        {
            new(ContextFiles.Instruction, template.Content),
            Text(ContextFiles.Task, JsonSerializer.Serialize(TaskDocument.From(input), ContextJson.Default.TaskDocument) + "\n"),
        };

        if (input.Issue is { } issue)
        {
            files.Add(Text(ContextFiles.Issue, $"# {issue.Title}\n\n{issue.Body}\n"));
        }

        if (input.Comments is { Count: > 0 } comments)
        {
            files.Add(Text(ContextFiles.Comments, JsonSerializer.Serialize(comments.Select(CommentDocument.From).ToList(), ContextJson.Default.ListCommentDocument) + "\n"));
        }

        AddIfPresent(files, ContextFiles.Plan, input.Plan);
        AddIfPresent(files, ContextFiles.Findings, input.Findings);
        AddIfPresent(files, ContextFiles.Validation, input.Validation);
        AddIfPresent(files, ContextFiles.Handoff, input.Handoff);
        if (schema is not null)
        {
            files.Add(Text(ContextFiles.ResultSchema, schema + "\n"));
        }

        return new AssembledContext(files, methodology.Files, methodology.Name, template.Identity, schema);
    }

    private static void AddIfPresent(List<WorkspaceFile> files, string name, string? content)
    {
        if (!string.IsNullOrEmpty(content))
        {
            files.Add(Text(name, content));
        }
    }

    private static WorkspaceFile Text(string name, string content) => new(name, Utf8.GetBytes(content));
}
