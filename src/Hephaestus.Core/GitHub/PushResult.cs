namespace Hephaestus.Core.GitHub;

/// <summary>A check the gateway makes before it pushes (AD-9, AD-19); the tag is what an Escalation names and the journal records, and is never translated.</summary>
public enum PushCheck
{
    /// <summary>The bundle is not the file that was written, or git's strict object checking refused what it holds.</summary>
    Import,

    /// <summary>The bundle carries a ref that is not the Task's branch, or more than one.</summary>
    Branch,

    /// <summary>The branch on GitHub is not at the tip Hephaestus last pushed: someone else pushed to it, or deleted it.</summary>
    RemoteMoved,

    /// <summary>The new tip does not descend from the last pushed tip.</summary>
    FastForward,

    /// <summary>A stored secret value is in a new commit, tree, or blob.</summary>
    Secret,

    /// <summary>The diff against the Task's start commit touches a protected path.</summary>
    ProtectedPath,
}

public static class PushChecks
{
    public static string Tag(this PushCheck check) => check switch
    {
        PushCheck.Import => "import",
        PushCheck.Branch => "branch",
        PushCheck.RemoteMoved => "remote-moved",
        PushCheck.FastForward => "fast-forward",
        PushCheck.Secret => "secret",
        PushCheck.ProtectedPath => "protected-path",
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, null),
    };

    public static bool TryParse(string? tag, out PushCheck check)
    {
        foreach (var candidate in Enum.GetValues<PushCheck>())
        {
            if (candidate.Tag() == tag)
            {
                check = candidate;
                return true;
            }
        }

        check = default;
        return false;
    }
}

/// <summary>How a push ended.</summary>
public enum PushOutcome
{
    /// <summary>The branch moved forward to the tip.</summary>
    Pushed,

    /// <summary>The branch on GitHub was at the tip, or beyond it, already: an earlier attempt was made and not recorded, so nothing was pushed (AD-5).</summary>
    AlreadyPushed,

    /// <summary>A check failed and nothing was pushed; <see cref="PushResult.Check"/> names it.</summary>
    Blocked,

    /// <summary>The push could not be made for a reason that is not the work's; <see cref="PushResult.Failure"/> says why.</summary>
    Failed,
}

/// <summary>
/// The outcome of a push. <paramref name="Detail"/> is what the check found, as facts and not as a sentence: the paths, the commit ids, or git's own report; it is masked and bounded, and
/// never holds a secret value.
/// </summary>
public sealed record PushResult(PushOutcome Outcome, PushCheck? Check, string? Detail, MirrorFailure Failure = MirrorFailure.None)
{
    public static PushResult Pushed() => new(PushOutcome.Pushed, null, null);

    public static PushResult AlreadyPushed() => new(PushOutcome.AlreadyPushed, null, null);

    public static PushResult Blocked(PushCheck check, string? detail = null) => new(PushOutcome.Blocked, check, detail);

    public static PushResult Failed(MirrorFailure failure, string? detail = null) => new(PushOutcome.Failed, null, detail, failure);
}

/// <summary>Where a Task branch is, compared with the tip Hephaestus last pushed.</summary>
public enum BranchState
{
    /// <summary>It is at the tip, or absent and nothing was pushed: a step may run, and a push may be made.</summary>
    InSync,

    /// <summary>It is not: someone else pushed to it, or deleted it. No step runs on it and nothing is pushed.</summary>
    Moved,

    /// <summary>The check could not be made; <see cref="BranchCheckResult.Failure"/> says why.</summary>
    Failed,
}

/// <summary>The outcome of looking at a Task branch. <paramref name="Detail"/> names, for a branch that moved, the commits Hephaestus did not push.</summary>
public sealed record BranchCheckResult(BranchState State, string? Detail, MirrorFailure Failure = MirrorFailure.None)
{
    public static BranchCheckResult InSync() => new(BranchState.InSync, null);

    public static BranchCheckResult Moved(string? detail) => new(BranchState.Moved, detail);

    public static BranchCheckResult Unavailable(MirrorFailure failure, string? detail = null) => new(BranchState.Failed, detail, failure);
}
