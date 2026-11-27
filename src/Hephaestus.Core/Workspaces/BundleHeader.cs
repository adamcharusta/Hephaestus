using System.Text;

namespace Hephaestus.Core.Workspaces;

/// <summary>
/// The header of a git bundle (AD-9): what it says it holds and what it needs, read as text. The runtime reads this from a file that came out of a container and runs no git on it, so what the
/// header says is a claim that the gateway's import checks again with git, in quarantine (Story 3.7). Format 2 and 3 are read; a line that is none of the header's lines makes the whole header unreadable.
/// The comment after a prerequisite's commit is the subject of that commit, in whatever encoding it was written, so it is never decoded and never has a length of its own to fail.
/// </summary>
public sealed record BundleHeader(IReadOnlyList<(string Commit, string Reference)> Heads, IReadOnlyList<string> Prerequisites)
{
    /// <summary>The most the header may take: a line is a commit id and a reference name or a subject, and a bundle of one commit has two or three.</summary>
    public const int MaximumBytes = 64 * 1024;

    private static readonly byte[] V2 = "# v2 git bundle"u8.ToArray();
    private static readonly byte[] V3 = "# v3 git bundle"u8.ToArray();

    /// <summary>Reads the header from the start of a bundle, or null when the stream does not start with one of the two signatures, has a line that is not a header line, or has no end of header within <see cref="MaximumBytes"/>.</summary>
    public static BundleHeader? TryRead(Stream bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var buffer = new byte[MaximumBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = bundle.Read(buffer, length, buffer.Length - length);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        var end = buffer.AsSpan(0, length).IndexOf("\n\n"u8);
        if (end < 0)
        {
            return null;
        }

        var header = buffer.AsSpan(0, end);
        var firstEnd = header.IndexOf((byte)'\n');
        var signature = firstEnd < 0 ? header : header[..firstEnd];
        var version3 = signature.SequenceEqual(V3);
        if (!version3 && !signature.SequenceEqual(V2))
        {
            return null;
        }

        var heads = new List<(string, string)>();
        var prerequisites = new List<string>();
        var rest = firstEnd < 0 ? default : header[(firstEnd + 1)..];
        while (!rest.IsEmpty)
        {
            var lineEnd = rest.IndexOf((byte)'\n');
            var line = lineEnd < 0 ? rest : rest[..lineEnd];
            rest = lineEnd < 0 ? default : rest[(lineEnd + 1)..];
            if (!TryReadLine(line, version3, heads, prerequisites))
            {
                return null;
            }
        }

        return new BundleHeader(heads, prerequisites);
    }

    private static bool TryReadLine(ReadOnlySpan<byte> line, bool version3, List<(string, string)> heads, List<string> prerequisites)
    {
        if (version3 && line.Length > 0 && line[0] == (byte)'@')
        {
            return true;
        }

        if (line.Length >= 41 && line[0] == (byte)'-' && BundleReference.IsCommitId(Encoding.ASCII.GetString(line[1..41])) && (line.Length == 41 || line[41] == (byte)' '))
        {
            prerequisites.Add(Encoding.ASCII.GetString(line[1..41]));
            return true;
        }

        if (line.Length > 41 && line[40] == (byte)' ' && BundleReference.IsCommitId(Encoding.ASCII.GetString(line[..40])) && !line.Contains((byte)'\r') && !line.Contains((byte)0))
        {
            try
            {
                heads.Add((Encoding.ASCII.GetString(line[..40]), new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(line[41..])));
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return false;
    }
}
