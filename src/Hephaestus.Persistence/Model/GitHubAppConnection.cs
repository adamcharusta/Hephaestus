namespace Hephaestus.Persistence.Model;

/// <summary>
/// The id of Hephaestus's own GitHub App (AD-4), Installation Configuration. There is one row, <see cref="TheOnlyId"/>. The private
/// key is a secret file and is never part of the database (AD-16).
/// </summary>
internal sealed class GitHubAppConnection
{
    public const int TheOnlyId = 1;

    public int Id { get; set; } = TheOnlyId;

    public long AppId { get; set; }

    /// <summary>The App's slug, from which the login of its bot account follows; null for an App connected before the slug was kept.</summary>
    public string? AppSlug { get; set; }

    public DateTimeOffset ConnectedAt { get; set; }
}
