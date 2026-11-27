using System.Globalization;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Workspaces;

/// <summary>
/// The whole of a checkpoint commit, as Core composes it (AD-9, AR-33): the identity and the message, so that the checkpoint container adds nothing of its own and an Agent decides nothing of it. The message
/// is a subject, resolved from a message id in the language of the Task, and the five trailers, which are never translated and are informational: attribution is read from the step run's records.
/// </summary>
public sealed record CheckpointCommit
{
    public const string TaskTrailer = "Hephaestus-Task";
    public const string StepTrailer = "Hephaestus-Step";
    public const string AgentTrailer = "Hephaestus-Agent";
    public const string ModelTrailer = "Hephaestus-Model";
    public const string EffortTrailer = "Hephaestus-Effort";

    private CheckpointCommit(CommitIdentity identity, string message, Guid taskId, Guid stepRunId, StepKind kind)
    {
        Identity = identity;
        Message = message;
        TaskId = taskId;
        StepRunId = stepRunId;
        Kind = kind;
    }

    /// <summary>The Task, the step run, and the kind of step the commit was composed for, which its trailers and subject say; a request for a checkpoint checks them against its own.</summary>
    public Guid TaskId { get; }

    public Guid StepRunId { get; }

    public StepKind Kind { get; }

    public CommitIdentity Identity { get; }

    /// <summary>The message in full, ending in a line feed.</summary>
    public string Message { get; }

    /// <summary>
    /// Composes the commit of the step run <paramref name="stepRunId"/> of the Task, which performed a step of <paramref name="kind"/>. <paramref name="model"/> is opaque to Hephaestus and is
    /// written as it is, so it must have the shape an Alias allows a Model.
    /// </summary>
    public static CheckpointCommit Compose(CommitIdentity identity, Language language, StepKind kind, Guid taskId, Guid stepRunId, Agent agent, string model, Effort effort)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!kind.IsCheckpointed())
        {
            throw new ArgumentException($"A step of kind '{kind.Tag()}' is not checkpointed.", nameof(kind));
        }

        if (!AliasShape.IsValidModel(model))
        {
            throw new ArgumentException("A model is named as a vendor writes it, so that it is one line with no space in it.", nameof(model));
        }

        var subject = MessageCatalog.Instance.Resolve(new Message(MessageIds.CheckpointSubject, kind.Tag()), language);
        var message = string.Join(
            '\n',
            subject,
            string.Empty,
            $"{TaskTrailer}: {taskId.ToString("D", CultureInfo.InvariantCulture)}",
            $"{StepTrailer}: {stepRunId.ToString("D", CultureInfo.InvariantCulture)}",
            $"{AgentTrailer}: {agent.Tag()}",
            $"{ModelTrailer}: {model}",
            $"{EffortTrailer}: {effort.Tag()}") + "\n";
        return new CheckpointCommit(identity, message, taskId, stepRunId, kind);
    }
}
