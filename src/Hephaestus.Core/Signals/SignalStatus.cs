namespace Hephaestus.Core.Signals;

/// <summary>What became of a Signal in the inbox (AR-26): it waits, the loop applied it, or the loop recorded it and changed nothing for it.</summary>
public enum SignalStatus
{
    /// <summary>Appended, not yet read by the loop.</summary>
    Pending,

    /// <summary>The loop applied it in one transaction. A Signal that concerns no Task yet is applied with no change.</summary>
    Applied,

    /// <summary>The loop recorded it with a reason and changed no Task for it.</summary>
    Rejected,
}

public static class SignalStatuses
{
    /// <summary>The name stored and shown for a status; never translated.</summary>
    public static string Tag(this SignalStatus status) => status switch
    {
        SignalStatus.Pending => "pending",
        SignalStatus.Applied => "applied",
        SignalStatus.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static bool TryParse(string? tag, out SignalStatus status)
    {
        foreach (var candidate in Enum.GetValues<SignalStatus>())
        {
            if (candidate.Tag() == tag)
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }
}

/// <summary>The reasons the loop gives for rejecting a Signal; machine values, never translated.</summary>
public static class SignalRejections
{
    /// <summary>The actor is not a Trusted User (AD-15).</summary>
    public const string ActorNotTrusted = "actor-not-trusted";

    /// <summary>GitHub's event data names no actor, for example a deleted account, so nobody can be trusted for it (AD-15).</summary>
    public const string ActorUnknown = "actor-unknown";

    /// <summary>The actor is Hephaestus's own GitHub identity, whose content is never admitted (AD-15).</summary>
    public const string OwnIdentity = "own-identity";

    /// <summary>The repository is not a registered Project, or was removed.</summary>
    public const string ProjectNotRegistered = "project-not-registered";

    /// <summary>The Signal carries no issue title, so there is no content that the event can vouch for.</summary>
    public const string SnapshotMissing = "snapshot-missing";

    /// <summary>Applying the Signal failed again and again; it is set aside so that the Signals behind it are not held back.</summary>
    public const string ApplyFailed = "apply-failed";
}

/// <summary>What an applied Signal says besides that it was applied, in the place a rejected one gives its reason; machine values, never translated.</summary>
public static class SignalNotes
{
    /// <summary>A request to start work on a Project whose window is open already: nothing was opened, and the command says so (FR-16).</summary>
    public const string WindowAlreadyOpen = "window-already-open";

    /// <summary>A request to stop a Project whose window is not open: nothing was closed.</summary>
    public const string WindowNotOpen = "window-not-open";
}
