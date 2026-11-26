using System.Text;

namespace Hephaestus.Core.Secrets;

/// <summary>
/// Masks text that is delivered in chunks. A tail that could be the beginning of a secret is held back until the next chunk shows
/// whether it is one, so a value that a chunk boundary splits is masked whole and no part of it is written. <see cref="Complete"/>
/// writes what is still held. One instance serves one stream and is not safe for concurrent use.
/// </summary>
public sealed class SecretMaskingStream
{
    private readonly SecretMasker _masker;
    private string _held = string.Empty;

    internal SecretMaskingStream(SecretMasker masker) => _masker = masker;

    /// <summary>The masked text that is safe to write now; what could still be part of a secret is kept for the next call.</summary>
    public string Write(ReadOnlySpan<char> chunk) => Process(chunk, isFinal: false);

    /// <summary>The masked rest of the stream, once no more text will come.</summary>
    public string Complete() => Process([], isFinal: true);

    private string Process(ReadOnlySpan<char> chunk, bool isFinal)
    {
        var text = string.Concat(_held, chunk);
        var output = new StringBuilder(text.Length);
        var consumed = _masker.Matcher.Scan(text, isFinal, SecretMasker.Replacement, output);
        _held = text[consumed..];
        return output.ToString();
    }
}
