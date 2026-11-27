namespace Hephaestus.Core.Secrets;

/// <summary>
/// Looks for a stored secret value, in the forms the masker knows, in text that arrives in chunks, such as a blob that is read as it comes (AD-9, AD-16). It finds a value that a chunk boundary splits, and it
/// remembers only as much of the end of the text as the longest form needs. One instance serves one text and is not safe for concurrent use.
/// </summary>
public sealed class SecretScan
{
    private readonly SecretMatcher _matcher;
    private string _tail = string.Empty;

    internal SecretScan(SecretMatcher matcher) => _matcher = matcher;

    /// <summary>Whether a secret was found in the text so far.</summary>
    public bool Found { get; private set; }

    /// <summary>Adds the next chunk; returns <see cref="Found"/>.</summary>
    public bool Write(ReadOnlySpan<char> chunk)
    {
        if (Found || _matcher.IsEmpty)
        {
            return Found;
        }

        var text = string.Concat(_tail, chunk);
        if (_matcher.Contains(text))
        {
            Found = true;
            _tail = string.Empty;
            return true;
        }

        // A form that is not whole in this text can only begin in its last LongestForm - 1 characters.
        var keep = Math.Min(text.Length, _matcher.LongestForm - 1);
        _tail = text[^keep..];
        return false;
    }
}
