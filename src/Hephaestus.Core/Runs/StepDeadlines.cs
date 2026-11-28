using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;

namespace Hephaestus.Core.Runs;

/// <summary>When the runtime stops a step that still runs (FR-19, AD-18): the loop records the deadline on the step run before its container is asked for, and the runtime alone acts on it.</summary>
public static class StepDeadlines
{
    /// <summary>How long a step of an Agent may run when neither the Project nor the installation limits a Task's duration.</summary>
    public static readonly TimeSpan DefaultAgentLimit = TimeSpan.FromHours(2);

    /// <summary>The time an Agent's step may run: the Project's limit, held to the installation's, else the installation's, else <see cref="DefaultAgentLimit"/>. A repository value never raises an installation limit (AD-16).</summary>
    public static TimeSpan AgentLimit(ProjectLimits requested, InstallationLimits installation)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(installation);
        var duration = requested.TaskDuration is { } asked
            ? installation.TaskDuration is { } allowed && allowed < asked ? allowed : asked
            : installation.TaskDuration;
        return duration ?? DefaultAgentLimit;
    }
}
