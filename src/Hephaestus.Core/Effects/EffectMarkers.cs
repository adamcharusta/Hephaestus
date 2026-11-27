namespace Hephaestus.Core.Effects;

/// <summary>
/// The hidden marker that is the last line of every comment Hephaestus posts (AD-5): <c>&lt;!-- hephaestus:effect=&lt;idempotency-key&gt; --&gt;</c>.
/// After a crash the comment is found by it. It is never translated.
/// </summary>
public static class EffectMarkers
{
    private const string Prefix = "<!-- hephaestus:effect=";
    private const string Suffix = " -->";

    /// <summary>The marker of the effect with <paramref name="idempotencyKey"/>.</summary>
    public static string For(string idempotencyKey) => Prefix + idempotencyKey + Suffix;

    /// <summary>The comment text with the marker as its last line, after one empty line.</summary>
    public static string Append(string text, string idempotencyKey) => text.TrimEnd() + "\n\n" + For(idempotencyKey);

    /// <summary>Whether the last line of <paramref name="body"/> is the marker of <paramref name="idempotencyKey"/>; a marker elsewhere in the text is not one.</summary>
    public static bool EndsWith(string? body, string idempotencyKey)
    {
        if (body is null)
        {
            return false;
        }

        var trimmed = body.TrimEnd();
        var last = trimmed[(trimmed.LastIndexOf('\n') + 1)..].Trim();
        return string.Equals(last, For(idempotencyKey), StringComparison.Ordinal);
    }
}
