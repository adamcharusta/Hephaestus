namespace Hephaestus.Core.Trust;

/// <summary>What the admission function decided about the actor of a Signal.</summary>
public enum Admitted
{
    /// <summary>The actor is a Trusted User.</summary>
    Trusted,

    /// <summary>The actor is a user, and the Owner does not trust them.</summary>
    NotTrusted,

    /// <summary>GitHub's event data names no actor, so nobody can be trusted for the event.</summary>
    NoActor,

    /// <summary>The actor is Hephaestus's own GitHub identity, which is never a Trusted User and whose content is never admitted.</summary>
    OwnIdentity,
}

/// <summary>
/// The one admission function (AD-15): whether the actor of a Signal is a Trusted User. The decision is by GitHub's numeric id; the
/// login is used for nothing but to recognise Hephaestus's own bot account, which no id in the list can be.
/// </summary>
public static class Admission
{
    /// <param name="actorId">The numeric id GitHub's own event data names; null when it names none.</param>
    /// <param name="actorLogin">The actor's login as the event data showed it.</param>
    /// <param name="trustedUserIds">The numeric ids of the Trusted Users.</param>
    /// <param name="ownLogin">The login of Hephaestus's own bot account; null when it is not known.</param>
    public static Admitted Decide(long? actorId, string? actorLogin, IReadOnlySet<long> trustedUserIds, string? ownLogin)
    {
        if (ownLogin is not null && actorLogin is not null && GitHubLogin.SameLogin(actorLogin, ownLogin))
        {
            return Admitted.OwnIdentity;
        }

        if (actorId is not { } id)
        {
            return Admitted.NoActor;
        }

        return trustedUserIds.Contains(id) ? Admitted.Trusted : Admitted.NotTrusted;
    }
}
