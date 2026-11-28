using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;

namespace Hephaestus.Core.Runs;

/// <summary>When the runtime stops a step that still runs (FR-19, AD-18): the loop records the deadline on the step run before its container is asked for, and the runtime alone acts on it.</summary>
public static class StepDeadlines
{
    /// <summary>How long a step of an Agent may run when neither the Project nor the installation limits a Task's duration.</summary>
    public static readonly TimeSpan DefaultAgentLimit = TimeSpan.FromHours(2);

    /// <summary>The Task duration limit (NFR-5): the Project's limit, held to the installation's, else the installation's, else <see cref="DefaultAgentLimit"/>. A repository value never raises an installation limit (AD-16).</summary>
    public static TimeSpan TaskLimit(ProjectLimits requested, InstallationLimits installation)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(installation);
        var duration = requested.TaskDuration is { } asked
            ? installation.TaskDuration is { } allowed && allowed < asked ? allowed : asked
            : installation.TaskDuration;
        return duration ?? DefaultAgentLimit;
    }

    /// <summary>The time an Agent's step may run: the Task duration limit, as the step is the Task's run.</summary>
    public static TimeSpan AgentLimit(ProjectLimits requested, InstallationLimits installation) => TaskLimit(requested, installation);

    /// <summary>
    /// When the runtime stops a step planned at <paramref name="now"/> that may run for <paramref name="stepLimit"/>: never later than the moment the Task's time in <c>Running</c> reaches <paramref name="taskLimit"/>, given the time
    /// <paramref name="used"/> it has spent already (NFR-5). A Task that has none left gets a deadline of <paramref name="now"/>, which the runtime acts on at its first look.
    /// </summary>
    public static DateTimeOffset For(DateTimeOffset now, TimeSpan stepLimit, TimeSpan taskLimit, TimeSpan used)
    {
        var remaining = taskLimit - used;
        var allowed = remaining < stepLimit ? remaining : stepLimit;
        return now + (allowed < TimeSpan.Zero ? TimeSpan.Zero : allowed);
    }

    /// <summary>How much of the Task duration limit is left to a Task that has spent <paramref name="used"/> of it in <c>Running</c>.</summary>
    public static TimeSpan Remaining(TimeSpan taskLimit, TimeSpan used) => taskLimit - used;
}
