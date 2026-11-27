using System.Text.RegularExpressions;

namespace Hephaestus.Core.Workspaces;

/// <summary>
/// A git bundle on the data volume (AD-9): the one way code crosses the boundary of a Workspace. The producer writes the file and the reference, the consumer reads
/// the file and deletes it. The reference names the file by its path under the bundles directory, never by an absolute path, and says what the file must hold: its SHA-256
/// and the one commit it carries, so that a consumer finds out that the file is not what was written before it hands the file to anything.
/// </summary>
/// <param name="Path">The file, relative to the bundles directory: forward slashes, no empty, <c>.</c>, or <c>..</c> name, ending in <c>.bundle</c>.</param>
/// <param name="Sha256">The SHA-256 of the file, 64 lower-case hexadecimal digits.</param>
/// <param name="Tip">The commit the bundle carries, 40 lower-case hexadecimal digits: the mirror and the checkout both make SHA-1 repositories, which is what GitHub serves.</param>
/// <param name="Prerequisite">The one commit a bundle that is not whole needs the consumer to have: the checkpoint of a step carries only its commit, and its prerequisite is the last pushed tip it was made on. Null for a bundle that is whole.</param>
public sealed partial record BundleReference
{
    public const string Extension = ".bundle";

    /// <summary>The largest bundle the gateway writes and the runtime hands to a container, so that the history of one repository cannot fill the data volume with one step's checkout.</summary>
    public const long DefaultMaximumBytes = 2L * 1024 * 1024 * 1024;

    public BundleReference(string path, string sha256, string tip, string? prerequisite = null)
    {
        if (!WorkspaceFile.IsContained(path) || !path.EndsWith(Extension, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{path}' is not a relative path of a {Extension} file.", nameof(path));
        }

        if (!Sha256Pattern().IsMatch(sha256))
        {
            throw new ArgumentException("A checksum is the SHA-256 of the file in 64 lower-case hexadecimal digits.", nameof(sha256));
        }

        if (!TipPattern().IsMatch(tip))
        {
            throw new ArgumentException("A tip is a commit id in 40 lower-case hexadecimal digits.", nameof(tip));
        }

        if (prerequisite is not null && !TipPattern().IsMatch(prerequisite))
        {
            throw new ArgumentException("A prerequisite is a commit id in 40 lower-case hexadecimal digits.", nameof(prerequisite));
        }

        Path = path;
        Sha256 = sha256;
        Tip = tip;
        Prerequisite = prerequisite;
    }

    public string Path { get; }

    public string Sha256 { get; }

    public string Tip { get; }

    public string? Prerequisite { get; }

    /// <summary>Whether the text is a commit id: 40 lower-case hexadecimal digits.</summary>
    public static bool IsCommitId(string? text) => text is not null && TipPattern().IsMatch(text);

    [GeneratedRegex(@"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex(@"^[0-9a-f]{40}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TipPattern();
}
