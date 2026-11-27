using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces;

/// <summary>
/// The labels and names of the Docker objects the runtime creates (AD-5, AR-34). Every container, volume, and network carries the three
/// labels of the convention, whose values are empty where the scope does not apply (a Task's volume belongs to no one step run). Recovery and cleanup
/// select on the installation label, and an object left by a crash is found again by the step-run label (AD-5).
/// </summary>
public static class WorkspaceLabels
{
    public const string Installation = "hephaestus.installation";
    public const string Task = "hephaestus.task";
    public const string StepRun = "hephaestus.step-run";

    /// <summary>What a container is for: <see cref="StepRole"/>, or one of the inert helpers that are created and never started.</summary>
    public const string Role = "hephaestus.role";

    /// <summary>The idempotency key of the effect that made the object (AD-5), so that the journal and Docker can be read against each other.</summary>
    public const string Effect = "hephaestus.effect";

    public const string StepRole = "step";
    public const string EgressProxyRole = "egress-proxy";
    public const string ProbeRole = "image-probe";
    public const string VolumeHelperRole = "volume-helper";
    public const string FillRole = "file-fill";

    public static string TaskVolume(Guid taskId) => $"{StepContainerLayout.NamePrefix}ws-{taskId:D}";

    public static string StepContainer(Guid stepRunId) => $"{StepContainerLayout.NamePrefix}step-{stepRunId:D}";

    /// <summary>The internal network of one step run: the step container and its egress proxy are on it and nothing else is, which keeps step containers apart (AD-7).</summary>
    public static string StepNetwork(Guid stepRunId) => $"{StepContainerLayout.NamePrefix}net-{stepRunId:D}";

    /// <summary>The egress proxy of one step run.</summary>
    public static string EgressProxy(Guid stepRunId) => $"{StepContainerLayout.NamePrefix}egress-{stepRunId:D}";

    public static string ContextVolume(Guid stepRunId) => $"{StepContainerLayout.NamePrefix}context-{stepRunId:D}";

    public static string MethodologyVolume(Guid stepRunId) => $"{StepContainerLayout.NamePrefix}methodology-{stepRunId:D}";

    /// <summary>The name of an inert helper container, which is named after what it is for and what it works on, so that one left by a crash is replaced rather than collided with.</summary>
    public static string Helper(string role, Guid id) => $"{StepContainerLayout.NamePrefix}{role}-{id:D}";

    public static Dictionary<string, string> For(string installationId, Guid? taskId, Guid? stepRunId, string idempotencyKey, string? role = null)
    {
        var labels = new Dictionary<string, string>
        {
            [Installation] = installationId,
            [Task] = taskId?.ToString("D") ?? string.Empty,
            [StepRun] = stepRunId?.ToString("D") ?? string.Empty,
            [Effect] = idempotencyKey,
        };
        if (role is not null)
        {
            labels[Role] = role;
        }

        return labels;
    }
}
