namespace Hephaestus.Core.GitHub;

/// <summary>
/// Where the GitHub App credential lives (AD-4, AD-16). The private key is a secret under this name; the App id is Installation
/// Configuration. Only <c>Hephaestus.GitHub</c> reads the key, and an architecture test says so.
/// </summary>
public static class GitHubAppCredential
{
    public const string PrivateKeySecretName = "github-app-private-key";
}
