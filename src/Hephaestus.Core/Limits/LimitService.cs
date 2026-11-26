using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Limits;

/// <summary>Why the limits were not changed.</summary>
public enum LimitFailure
{
    /// <summary>They were.</summary>
    None,

    /// <summary>No limit was named, so there is nothing to change.</summary>
    NothingGiven,

    InvalidTaskDuration,
    InvalidCpus,
    InvalidMemory,
}

/// <summary>The outcome of setting limits: the limits as stored now, or the first value that was refused.</summary>
public sealed record LimitResult(LimitFailure Failure, InstallationLimits? Limits)
{
    public static LimitResult Failed(LimitFailure failure) => new(failure, null);

    public static LimitResult Done(InstallationLimits limits) => new(LimitFailure.None, limits);
}

/// <summary>The rules for the installation limits: each value has one shape and a range, and a change names the limits it changes and nothing else.</summary>
public sealed class LimitService(IInstallationConfiguration configuration)
{
    public async Task<LimitResult> SetAsync(string? taskDuration, string? cpus, string? memory, CancellationToken cancellationToken)
    {
        if (taskDuration is null && cpus is null && memory is null)
        {
            return LimitResult.Failed(LimitFailure.NothingGiven);
        }

        TimeSpan? duration = null;
        if (taskDuration is not null)
        {
            if (!LimitFormat.TryParseDuration(taskDuration, out var parsed))
            {
                return LimitResult.Failed(LimitFailure.InvalidTaskDuration);
            }

            duration = parsed;
        }

        int? cpuMillis = null;
        if (cpus is not null)
        {
            if (!LimitFormat.TryParseCpus(cpus, out var parsed))
            {
                return LimitResult.Failed(LimitFailure.InvalidCpus);
            }

            cpuMillis = parsed;
        }

        long? memoryBytes = null;
        if (memory is not null)
        {
            if (!LimitFormat.TryParseMemory(memory, out var parsed))
            {
                return LimitResult.Failed(LimitFailure.InvalidMemory);
            }

            memoryBytes = parsed;
        }

        return LimitResult.Done(await configuration.SetLimitsAsync(new LimitsChange(duration, cpuMillis, memoryBytes), cancellationToken));
    }
}
