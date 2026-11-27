using System.Security.Cryptography;
using System.Text;

namespace Hephaestus.Core.Tasks;

/// <summary>The identity of an issue's content (AD-15): an edit is detected by comparing this hash with the one recorded when the content was admitted.</summary>
public static class IssueContent
{
    /// <summary>
    /// The SHA-256 of the title and the body, in lower-case hexadecimal. The title's length leads, so that no pair of title and body
    /// can give the hash of another pair by moving text between them. The text is hashed as GitHub gave it, with no normalization.
    /// </summary>
    public static string Hash(string title, string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{title.Length}:{title}{body}"));
        return Convert.ToHexStringLower(bytes);
    }
}
