namespace Hephaestus.Core.Invocations;

/// <summary>How an Agent's CLI signs in (FR-25, AD-10): with an API key, or with the token of a subscription. An adapter declares which it supports.</summary>
public enum AuthenticationMode
{
    ApiKey,
    Subscription,
}

public static class AuthenticationModes
{
    /// <summary>The name a command, a document, or a configuration file carries; never translated.</summary>
    public static string Tag(this AuthenticationMode mode) => mode switch
    {
        AuthenticationMode.ApiKey => "api-key",
        AuthenticationMode.Subscription => "subscription",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static bool TryParse(string? tag, out AuthenticationMode mode)
    {
        var normalized = tag?.Trim().ToLowerInvariant();
        foreach (var candidate in Enum.GetValues<AuthenticationMode>())
        {
            if (candidate.Tag() == normalized)
            {
                mode = candidate;
                return true;
            }
        }

        mode = default;
        return false;
    }

    /// <summary>The tags in the order a message lists them.</summary>
    public static IReadOnlyList<string> Tags { get; } = [.. Enum.GetValues<AuthenticationMode>().Select(Tag)];
}
