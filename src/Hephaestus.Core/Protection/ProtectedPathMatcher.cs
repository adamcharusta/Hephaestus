namespace Hephaestus.Core.Protection;

/// <summary>
/// Decides whether a repository path is protected (AD-19). A glob is matched against the whole path, from the root of the repository, one <c>/</c>-separated segment at a time: <c>**</c> as a segment stands for zero or more
/// whole segments, so <c>.github/**</c> protects everything under <c>.github</c> and the name <c>.github</c> itself; inside a segment <c>*</c> stands for any run of characters and <c>?</c> for one, and neither crosses a <c>/</c>.
/// Matching ignores case: a repository is checked out on file systems that do, and <c>Hephaestus.yml</c> is the file <c>hephaestus.yml</c> there, so a protected path is protected under every spelling.
/// </summary>
public static class ProtectedPathMatcher
{
    /// <summary>The globs of <paramref name="globs"/> that protect <paramref name="path"/>; empty when it is not protected. A glob that is not a valid one protects nothing.</summary>
    public static IReadOnlyList<string> Matching(string path, IEnumerable<string> globs)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(globs);
        var segments = path.Split('/');
        return [.. globs.Where(glob => ProtectedPath.IsValid(glob) && Matches(glob.Split('/'), 0, segments, 0))];
    }

    /// <summary>Whether any glob protects the path.</summary>
    public static bool IsProtected(string path, IEnumerable<string> globs) => Matching(path, globs).Count > 0;

    private static bool Matches(string[] glob, int g, string[] path, int p)
    {
        while (g < glob.Length)
        {
            if (glob[g] == "**")
            {
                // A run of ** is one: it can only stand for what the first would.
                while (g + 1 < glob.Length && glob[g + 1] == "**")
                {
                    g++;
                }

                for (var skip = p; skip <= path.Length; skip++)
                {
                    if (Matches(glob, g + 1, path, skip))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (p >= path.Length || !SegmentMatches(glob[g], path[p]))
            {
                return false;
            }

            g++;
            p++;
        }

        return p == path.Length;
    }

    /// <summary>One segment against one glob segment: <c>*</c> any run, <c>?</c> one character, everything else by itself, ignoring case. The usual two-pointer match with one place to go back to.</summary>
    private static bool SegmentMatches(string glob, string text)
    {
        var g = 0;
        var t = 0;
        var star = -1;
        var mark = 0;
        while (t < text.Length)
        {
            if (g < glob.Length && (glob[g] == '?' || char.ToUpperInvariant(glob[g]) == char.ToUpperInvariant(text[t])) && glob[g] != '*')
            {
                g++;
                t++;
            }
            else if (g < glob.Length && glob[g] == '*')
            {
                star = g++;
                mark = t;
            }
            else if (star >= 0)
            {
                g = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (g < glob.Length && glob[g] == '*')
        {
            g++;
        }

        return g == glob.Length;
    }
}
