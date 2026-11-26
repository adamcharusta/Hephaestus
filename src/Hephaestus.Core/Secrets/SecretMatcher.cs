using System.Buffers;
using System.Net;
using System.Text;

namespace Hephaestus.Core.Secrets;

/// <summary>
/// An immutable snapshot of what the masker looks for: each secret value as it is, base64-encoded, and URL-encoded (AD-16).
/// A snapshot is replaced as a whole when a secret changes, so a scan never sees half of an update.
/// </summary>
internal sealed class SecretMatcher
{
    public static readonly SecretMatcher Empty = new([]);

    // Longest first, so that where two forms start at the same place the longer one is masked whole.
    private readonly string[] _forms;
    private readonly SearchValues<char> _firstCharacters;

    public SecretMatcher(IEnumerable<string> values)
    {
        var forms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.SelectMany(value => new[] { value, SecretPolicy.TrimTrailingLineBreak(value) }))
        {
            // A value below the policy's minimum, or one that is only white space, would mask ordinary text, so a file that holds
            // one is ignored. The value without its trailing line break is a form too: the command line never stores a line break,
            // but a file put on the volume by hand may end in one, and a log line holds the credential without it.
            if (value.Length < SecretPolicy.MinimumValueLength || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
            forms.Add(value);
            forms.Add(base64);
            forms.Add(base64.TrimEnd('='));
            forms.Add(Uri.EscapeDataString(value));
            forms.Add(WebUtility.UrlEncode(value));
        }

        _forms = [.. forms.OrderByDescending(form => form.Length).ThenBy(form => form, StringComparer.Ordinal)];
        _firstCharacters = SearchValues.Create([.. _forms.Select(form => form[0]).Distinct()]);
    }

    public bool IsEmpty => _forms.Length == 0;

    /// <summary>
    /// Appends <paramref name="text"/> to <paramref name="output"/> with every run that holds a secret replaced by
    /// <paramref name="replacement"/>, and returns how many characters it consumed. Forms that overlap are masked as one run, so
    /// no tail of a longer value is left behind. Unless <paramref name="isFinal"/>, a tail that could still become a secret once
    /// more text arrives is not consumed; the caller keeps it and offers it again with the next chunk.
    /// </summary>
    public int Scan(ReadOnlySpan<char> text, bool isFinal, string replacement, StringBuilder output)
    {
        var position = 0;
        while (position < text.Length)
        {
            var next = text[position..].IndexOfAny(_firstCharacters);
            if (next < 0)
            {
                output.Append(text[position..]);
                return text.Length;
            }

            output.Append(text.Slice(position, next));
            position += next;

            var end = EndOfRun(text, position, isFinal);
            if (end < 0)
            {
                return position;
            }

            if (end == position)
            {
                output.Append(text[position]);
                position++;
                continue;
            }

            output.Append(replacement);
            position = end;
        }

        return position;
    }

    /// <summary>
    /// Where the masked run that starts at <paramref name="start"/> ends; <paramref name="start"/> itself when nothing matches
    /// there; -1 when the text so far is the beginning of a form and the answer depends on what comes next.
    /// </summary>
    private int EndOfRun(ReadOnlySpan<char> text, int start, bool isFinal)
    {
        var end = start;
        var index = start;
        do
        {
            var rest = text[index..];
            var length = LongestMatch(rest);
            if (length > 0)
            {
                end = Math.Max(end, index + length);
            }

            if (!isFinal && BeginsALongerForm(rest))
            {
                return -1;
            }

            index++;
        }
        while (index < end);

        return end;
    }

    private int LongestMatch(ReadOnlySpan<char> rest)
    {
        foreach (var form in _forms)
        {
            if (rest.StartsWith(form, StringComparison.Ordinal))
            {
                return form.Length;
            }
        }

        return 0;
    }

    private bool BeginsALongerForm(ReadOnlySpan<char> rest)
    {
        foreach (var form in _forms)
        {
            if (form.Length > rest.Length && form.AsSpan().StartsWith(rest, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
