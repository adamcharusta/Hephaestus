using System.Text.Json;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;

namespace Hephaestus.Core.Validation;

/// <summary>
/// The Validation of one pushed Task branch tip, one command after another (AD-13, FR-31): the commands of the Task's configuration snapshot, in order, and what the runtime saw of each. It
/// is the only thing that decides whether work passes. It accepts nothing but a command's result, which is made from the exit status and cause of its container, so no result an Agent returned,
/// and nothing it said of its own work, can reach it. A command is started only when every command before it passed, so the first that does not pass ends Validation and the rest are
/// recorded as not run. Immutable: <see cref="Record"/> returns the run with one more result.
/// </summary>
public sealed class ValidationRun
{
    /// <summary>How long one command may run when neither the Project nor the installation sets a duration (FR-19).</summary>
    public static readonly TimeSpan DefaultCommandLimit = TimeSpan.FromMinutes(30);

    private readonly ValidationCommandResult[] _recorded;

    private ValidationRun(IReadOnlyList<string> commands, ValidationCommandResult[] recorded)
    {
        Commands = commands;
        _recorded = recorded;
    }

    /// <summary>The Validation commands, in the order they run.</summary>
    public IReadOnlyList<string> Commands { get; }

    /// <summary>The results of the commands that were started, in order.</summary>
    public IReadOnlyList<ValidationCommandResult> Recorded => _recorded;

    /// <summary>The position of the command to start next; null when Validation is decided.</summary>
    public int? NextIndex => IsDecided ? null : _recorded.Length;

    /// <summary>The command to start next; null when Validation is decided.</summary>
    public string? NextCommand => NextIndex is { } index ? Commands[index] : null;

    /// <summary>Whether Validation has ended: a command did not pass, or every command did.</summary>
    public bool IsDecided => _recorded.Any(result => result.Status != ValidationCommandStatus.Passed) || _recorded.Length == Commands.Count;

    /// <summary>How Validation ended, or null while a command is still to run.</summary>
    public ValidationOutcome? Outcome => _recorded.FirstOrDefault(result => result.Status != ValidationCommandStatus.Passed)?.Status switch
    {
        ValidationCommandStatus.Failed => ValidationOutcome.ValidationFailure,
        ValidationCommandStatus.TimedOut => ValidationOutcome.Timeout,
        ValidationCommandStatus.Interrupted => ValidationOutcome.InfrastructureError,
        _ => _recorded.Length == Commands.Count ? ValidationOutcome.Passed : null,
    };

    /// <summary>The result of every command, in order, once Validation is decided: the commands that were not started are recorded as not run.</summary>
    public IReadOnlyList<ValidationCommandResult> Results =>
        IsDecided
            ? [.. _recorded, .. Commands.Select((command, index) => (command, index)).Skip(_recorded.Length).Select(pair => ValidationCommandResult.NotRun(pair.index, pair.command))]
            : throw new InvalidOperationException("A command is still to run, so what became of the rest is not yet known.");

    /// <summary>Begins the Validation of a branch tip. A Project always has a command (FR-2); an empty list would pass without checking anything.</summary>
    public static ValidationRun Begin(IReadOnlyList<string> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
        {
            throw new ArgumentException("Validation with no command would pass without checking anything.", nameof(commands));
        }

        if (commands.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A Validation command is not blank.", nameof(commands));
        }

        return new ValidationRun([.. commands], []);
    }

    /// <summary>
    /// How long one command may run before the runtime stops it (FR-19): the Task duration the Project asks for, within the installation's, else the installation's, else <see cref="DefaultCommandLimit"/>. A command is part of
    /// the Task's run, so it is never given more than the Task has.
    /// </summary>
    public static TimeSpan CommandLimit(ProjectLimits requested, InstallationLimits installation)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(installation);
        var duration = requested.TaskDuration is { } asked
            ? installation.TaskDuration is { } allowed && allowed < asked ? allowed : asked
            : installation.TaskDuration;
        return duration is { } limit && limit < DefaultCommandLimit ? limit : DefaultCommandLimit;
    }

    /// <summary>Records the result of the command that was started next. A result for another command, one that says the command was not run, or one after Validation was decided is a mistake of the caller.</summary>
    public ValidationRun Record(ValidationCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (NextIndex is not { } index)
        {
            throw new InvalidOperationException("Validation is decided: a further result changes nothing.");
        }

        if (result.Index != index || result.Command != Commands[index] || result.Status == ValidationCommandStatus.NotRun)
        {
            throw new ArgumentException($"The result is not that of command {index}, which is the one that was started next.", nameof(result));
        }

        return new ValidationRun(Commands, [.. _recorded, result]);
    }

    /// <summary>The content of <c>validation.json</c> (AD-12): the outcome, and for each command its status, exit status, duration, and the container run that keys its output. Written once Validation is decided.</summary>
    public string ToJson() => JsonSerializer.Serialize(ValidationDocument.From(this), ValidationJson.Default.ValidationDocument) + "\n";
}
