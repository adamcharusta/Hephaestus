using Hephaestus.Core.Localization;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Context;

/// <summary>
/// What Core knows when it assembles the context of one step run (AD-12). It names no Agent: the same step of the same Task gives the same files whichever Agent performs it (FR-62).
/// The issue and the comments are the admitted ones (AD-15), so that nothing else can enter a file; an input that does not exist is null or empty and has no file.
/// </summary>
/// <param name="TaskId">The Task.</param>
/// <param name="StepRunId">The step run the context is for.</param>
/// <param name="RepositoryId">GitHub's numeric id of the Project's repository.</param>
/// <param name="IssueNumber">The issue's number in that repository.</param>
/// <param name="Branch">The Task branch.</param>
/// <param name="StartCommit">The commit the step starts from.</param>
/// <param name="BaseBranch">The Project's Base Branch.</param>
/// <param name="Kind">The step kind.</param>
/// <param name="Round">The round of the step: 0 for the first, and the number of the Correction or Rework round after.</param>
/// <param name="Language">The Task's language (AD-21), which the template tells the Agent to write prose in.</param>
/// <param name="MethodologyName">The Methodology the Project's configuration names; null or blank when it names none, which is the built-in <c>default</c> (FR-61).</param>
/// <param name="Issue">The admitted snapshot of the issue.</param>
/// <param name="Comments">The admitted comments, oldest first.</param>
/// <param name="Plan">The plan, rendered by Core from the database.</param>
/// <param name="Findings">The Findings of the Review, as the JSON document Core wrote.</param>
/// <param name="Validation">The results of Validation, as the JSON document Core wrote.</param>
/// <param name="Handoff">The handoff summary, assembled by Core from recorded data; no Agent writes it.</param>
public sealed record StepContextInput(
    Guid TaskId,
    Guid StepRunId,
    long RepositoryId,
    int IssueNumber,
    string Branch,
    string StartCommit,
    string BaseBranch,
    StepKind Kind,
    int Round,
    Language Language,
    string? MethodologyName = null,
    IssueSnapshot? Issue = null,
    IReadOnlyList<AdmittedComment>? Comments = null,
    string? Plan = null,
    string? Findings = null,
    string? Validation = null,
    string? Handoff = null);

/// <summary>
/// The files of one step run, assembled (AD-12). The step run's record states <see cref="MethodologyName"/> and <see cref="Template"/> (AD-14).
/// </summary>
/// <param name="Context">The files placed read-only at <see cref="StepContainerLayout.Context"/>.</param>
/// <param name="Methodology">The Methodology's files, placed read-only at <see cref="StepContainerLayout.Methodology"/>.</param>
/// <param name="MethodologyName">The name of the Methodology applied.</param>
/// <param name="Template">The identity of the template applied.</param>
/// <param name="ResultSchema">The schema of the step's result, the content of <c>result.schema.json</c>; null for a kind with no result type yet.</param>
public sealed record AssembledContext(
    IReadOnlyList<WorkspaceFile> Context,
    IReadOnlyList<WorkspaceFile> Methodology,
    string MethodologyName,
    Methodologies.TemplateIdentity Template,
    string? ResultSchema);
