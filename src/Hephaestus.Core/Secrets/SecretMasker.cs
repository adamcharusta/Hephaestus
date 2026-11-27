using System.Text;
using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Secrets;

/// <summary>
/// The one masker (AD-16, AR-36): built from <see cref="ISecretStore"/>, it replaces every stored secret value, and the base64
/// and URL-encoded forms of it, by <see cref="Replacement"/> in the text a writer is about to write. It is rebuilt each time the
/// store reports a change, so a secret that is added, changed, or removed is masked, or no longer masked, without a restart.
/// Text that arrives in chunks goes through <see cref="CreateStream"/>, which finds a value that a chunk boundary splits.
/// </summary>
public sealed class SecretMasker
{
    /// <summary>What a secret is replaced by.</summary>
    public const string Replacement = "***";

    private readonly ISecretStore _source;
    private readonly SemaphoreSlim _rebuilding = new(1, 1);
    private volatile SecretMatcher _matcher = SecretMatcher.Empty;

    public SecretMasker(ISecretStore source)
    {
        _source = source;
        _source.Changed += () => RebuildAsync(CancellationToken.None);
    }

    internal SecretMatcher Matcher => _matcher;

    /// <summary>Reads every stored value and replaces what the masker looks for. The host calls it once before it writes a log line.</summary>
    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        await _rebuilding.WaitAsync(cancellationToken);
        try
        {
            _matcher = new SecretMatcher(await _source.ReadAllAsync(cancellationToken));
        }
        finally
        {
            _rebuilding.Release();
        }
    }

    /// <summary>The text with every secret replaced.</summary>
    public string Mask(string text)
    {
        var matcher = _matcher;
        if (matcher.IsEmpty || text.Length == 0)
        {
            return text;
        }

        var output = new StringBuilder(text.Length);
        matcher.Scan(text, isFinal: true, Replacement, output);
        return output.ToString();
    }

    /// <summary>A scan for one text delivered in chunks, for what is checked and not written: the masker's forms at the moment it is made.</summary>
    public SecretScan CreateScan() => new(_matcher);

    /// <summary>A masker for one stream of text delivered in chunks.</summary>
    public SecretMaskingStream CreateStream() => new(this);
}
