using System.Text.Json.Serialization;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Context;

/// <summary>The content of <c>task.json</c>: ids, branch, start commit, Base Branch, step kind, round, and language. Keys are never translated.</summary>
internal sealed record TaskDocument(
    Guid TaskId,
    Guid StepRunId,
    long RepositoryId,
    int IssueNumber,
    string Branch,
    string StartCommit,
    string BaseBranch,
    string StepKind,
    int Round,
    string Language)
{
    public static TaskDocument From(StepContextInput input) => new(
        input.TaskId, input.StepRunId, input.RepositoryId, input.IssueNumber, input.Branch, input.StartCommit, input.BaseBranch, input.Kind.Tag(), input.Round, Languages.Tag(input.Language));
}

/// <summary>One element of <c>comments.json</c>: what the admitted comment carries that an Agent can use, and not its hash or the Signal that vouched for it.</summary>
internal sealed record CommentDocument(long CommentId, string? AuthorLogin, string Body, Guid? AnswersEscalationId, DateTimeOffset AdmittedAt)
{
    public static CommentDocument From(AdmittedComment comment) => new(comment.CommentId, comment.AuthorLogin, comment.Body, comment.AnswersEscalationId, comment.AdmittedAt);
}

/// <summary>Source-generated JSON of the context files: camelCase keys, indented so that a person can read what an Agent was given.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(TaskDocument))]
[JsonSerializable(typeof(List<CommentDocument>))]
internal sealed partial class ContextJson : JsonSerializerContext;
