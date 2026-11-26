namespace Hephaestus.Persistence.Model;

/// <summary>
/// A Trusted User (AD-15). GitHub's numeric user id is the key and the identity; the login is for display and is refreshed when
/// the Owner adds the user again.
/// </summary>
internal sealed class TrustedUserEntry
{
    public long UserId { get; set; }

    public string Login { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; }
}
