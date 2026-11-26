using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Trust;

/// <summary>Why a change to the Trusted Users did not happen.</summary>
public enum TrustedUserFailure
{
    /// <summary>It happened.</summary>
    None,

    /// <summary>The login is not shaped like a GitHub login; GitHub was not asked.</summary>
    InvalidLogin,

    /// <summary>GitHub has no such user.</summary>
    UserNotFound,

    /// <summary>The login is Hephaestus's own GitHub identity, which is never a Trusted User (AD-15).</summary>
    OwnIdentity,

    /// <summary>The login is not a stored Trusted User.</summary>
    NotTrusted,

    /// <summary>No GitHub App is connected, so a login cannot be resolved.</summary>
    NotConnected,

    /// <summary>The stored App key cannot be read.</summary>
    KeyUnreadable,

    /// <summary>GitHub refused the stored credential.</summary>
    Rejected,

    /// <summary>GitHub did not answer.</summary>
    Unreachable,
}

/// <summary>The outcome of adding or removing a Trusted User.</summary>
/// <param name="User">The user that was added, found already stored, or removed.</param>
/// <param name="Changed">False when <c>add</c> found the user already stored.</param>
public sealed record TrustedUserResult(TrustedUserFailure Failure, TrustedUser? User, bool Changed)
{
    public static TrustedUserResult Failed(TrustedUserFailure failure) => new(failure, null, Changed: false);

    public static TrustedUserResult Done(TrustedUser user, bool changed) => new(TrustedUserFailure.None, user, changed);
}

/// <summary>
/// The rules for who the Owner trusts (AD-15): a Trusted User is a GitHub numeric id, found through the gateway and stored with the
/// login it had then, for display. Hephaestus's own identity is refused.
/// </summary>
public sealed class TrustedUserService(ICodeHost codeHost, IReadStore reads, IInstallationConfiguration configuration)
{
    public async Task<TrustedUserResult> AddAsync(string login, CancellationToken cancellationToken)
    {
        if (!GitHubLogin.IsValid(login))
        {
            return TrustedUserResult.Failed(TrustedUserFailure.InvalidLogin);
        }

        var lookup = await codeHost.FindUserAsync(login, cancellationToken);
        if (lookup.User is not { } found)
        {
            return TrustedUserResult.Failed(Map(lookup.Failure));
        }

        if (found.IsHephaestus)
        {
            return TrustedUserResult.Failed(TrustedUserFailure.OwnIdentity);
        }

        var (user, added) = await configuration.AddTrustedUserAsync(found.Id, found.Login, cancellationToken);
        return TrustedUserResult.Done(user, added);
    }

    public async Task<TrustedUserResult> RemoveAsync(string login, CancellationToken cancellationToken)
    {
        if (!GitHubLogin.IsValid(login))
        {
            return TrustedUserResult.Failed(TrustedUserFailure.InvalidLogin);
        }

        // What the Owner sees in the list is what is removed, and revoking trust does not depend on GitHub answering.
        var users = await reads.ListTrustedUsersAsync(cancellationToken);
        if (users.Count == 0)
        {
            return TrustedUserResult.Failed(TrustedUserFailure.NotTrusted);
        }

        var byLogin = users.Where(u => GitHubLogin.SameLogin(u.Login, login)).ToList();
        TrustedUser? target = byLogin.Count == 1 ? byLogin[0] : null;
        if (target is null)
        {
            // No stored login matches, or several do (a stale one beside the current one): GitHub says which id the login is now.
            var lookup = await codeHost.FindUserAsync(login, cancellationToken);
            if (lookup.User is not { } found)
            {
                return TrustedUserResult.Failed(lookup.Failure == GitHubUserFailure.NotFound ? TrustedUserFailure.NotTrusted : Map(lookup.Failure));
            }

            target = users.FirstOrDefault(u => u.UserId == found.Id);
            if (target is null)
            {
                return TrustedUserResult.Failed(TrustedUserFailure.NotTrusted);
            }
        }

        var removed = await configuration.RemoveTrustedUserAsync(target.UserId, cancellationToken);
        return removed ? TrustedUserResult.Done(target, changed: true) : TrustedUserResult.Failed(TrustedUserFailure.NotTrusted);
    }

    private static TrustedUserFailure Map(GitHubUserFailure failure) => failure switch
    {
        GitHubUserFailure.NotFound => TrustedUserFailure.UserNotFound,
        GitHubUserFailure.NotConnected => TrustedUserFailure.NotConnected,
        GitHubUserFailure.KeyUnreadable => TrustedUserFailure.KeyUnreadable,
        GitHubUserFailure.Rejected => TrustedUserFailure.Rejected,
        _ => TrustedUserFailure.Unreachable,
    };
}
