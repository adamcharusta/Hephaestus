using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hephaestus.Core.Tasks;

/// <summary>
/// The name of a Task's branch (FR-6): <c>hephaestus/&lt;issue-number&gt;-&lt;slug&gt;</c>, derived once from the issue's title when the
/// Task is created. The slug holds lower-case ASCII letters, digits, and single hyphens only, and is at most
/// <see cref="MaximumSlugLength"/> long; a title that gives none leaves the name <c>hephaestus/&lt;issue-number&gt;</c>.
/// </summary>
public static partial class BranchName
{
    public const string Prefix = "hephaestus/";

    public const int MaximumSlugLength = 40;

    // The lower-case Latin letters with a mark, as the ASCII letter they are read as (the Polish ones, and those of the other languages
    // of Western and Central Europe that a title is likely to hold). Nothing else is transliterated: any other character separates words.
    // String normalization cannot do this, because the build and the image run in globalization-invariant mode, where it changes nothing.
    internal const string MarkedLetters = "àáâãäåāăąçćčďđèéêëēęěìíîïīłńňñòóôõöøōőřśšťùúûüūůűýÿźżž";
    internal const string PlainLetters = "aaaaaaaaacccddeeeeeeeiiiiilnnnoooooooorsstuuuuuuuyyzzz";

    public static string For(int issueNumber, string title)
    {
        var number = issueNumber.ToString(CultureInfo.InvariantCulture);
        var slug = Slug(title);
        return slug.Length == 0 ? Prefix + number : $"{Prefix}{number}-{slug}";
    }

    /// <summary>Whether the name is one this class makes: the prefix, the issue number, and a slug of letters and digits in single-hyphen groups. Such a name is a valid branch name for git whatever its length, and holds nothing a shell or a refspec would read.</summary>
    public static bool IsTaskBranch(string? name) => name is not null && TaskBranchPattern().IsMatch(name);

    [GeneratedRegex(@"^hephaestus/[0-9]{1,10}(-[a-z0-9]{1,40}){0,20}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TaskBranchPattern();

    public static string Slug(string title)
    {
        var slug = new StringBuilder(MaximumSlugLength);
        var pendingHyphen = false;
        foreach (var character in title)
        {
            // A combining mark belongs to the letter before it, as in a title that is written in decomposed form.
            if (char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var letter = Fold(character);
            if (letter is not (>= 'a' and <= 'z') and not (>= '0' and <= '9'))
            {
                pendingHyphen = slug.Length > 0;
                continue;
            }

            if (slug.Length + (pendingHyphen ? 2 : 1) > MaximumSlugLength)
            {
                break;
            }

            if (pendingHyphen)
            {
                slug.Append('-');
                pendingHyphen = false;
            }

            slug.Append(letter);
        }

        return slug.ToString();
    }

    private static char Fold(char character)
    {
        var lower = char.ToLowerInvariant(character);
        var marked = MarkedLetters.IndexOf(lower);
        return marked >= 0 ? PlainLetters[marked] : lower;
    }
}
