using Hephaestus.Core.GitHub;

namespace Hephaestus.Core.Workspaces;

/// <summary>
/// Who a commit of Hephaestus is by (AD-9): the name and the address git is given for author and committer. A value that git would read as anything but one line of text,
/// or that holds an angle bracket, is refused, so that no identity can become a second header of the commit.
/// </summary>
public sealed record CommitIdentity
{
    public const int MaximumLength = 100;

    public CommitIdentity(string name, string email)
    {
        Require(name, nameof(name));
        Require(email, nameof(email));
        if (email.Count(character => character == '@') != 1 || email[0] == '@' || email[^1] == '@' || email.Contains(' ', StringComparison.Ordinal))
        {
            throw new ArgumentException("An address is a name, one @, and a host, with no space in it.", nameof(email));
        }
        Name = name;
        Email = email;
    }

    public string Name { get; }

    public string Email { get; }

    /// <summary>
    /// The identity of Hephaestus's own GitHub App (FR-30): the bot user GitHub gives it, <c>&lt;slug&gt;[bot]</c>, at the address GitHub reads as that user. The address carries no user id,
    /// because the App's slug is what GitHub reports; attribution is informational and is read from the step run's records, never from a commit (AD-9).
    /// </summary>
    public static CommitIdentity For(GitHubIdentity app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return ForSlug(app.Slug);
    }

    /// <summary>The identity of the GitHub App with this slug, which is all the loop stores of it (Story 1.7): the bot account's name and its no-reply address.</summary>
    public static CommitIdentity ForSlug(string slug)
    {
        var name = $"{slug}[bot]";
        return new CommitIdentity(name, $"{name}@users.noreply.github.com");
    }

    private static void Require(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLength || value != value.Trim() || value.Any(character => char.IsControl(character) || character is '<' or '>'))
        {
            throw new ArgumentException("An identity is one line of text, at most 100 characters, with no angle bracket and no space at either end.", parameter);
        }
    }
}
