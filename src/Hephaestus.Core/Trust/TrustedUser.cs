namespace Hephaestus.Core.Trust;

/// <summary>
/// A GitHub user whose labels, answers, and commands Hephaestus acts on (AD-15). The numeric id is the identity; the login is for
/// display only and is as old as the last time the Owner added the user, because a login can change hands.
/// </summary>
public sealed record TrustedUser(long UserId, string Login, DateTimeOffset AddedAt);

/// <summary>A Trusted User as stored, and whether this write created them (false: they were already stored and only the login was refreshed).</summary>
public readonly record struct TrustedUserWrite(TrustedUser User, bool Added);
