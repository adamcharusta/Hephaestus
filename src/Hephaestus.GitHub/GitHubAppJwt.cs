using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hephaestus.GitHub;

/// <summary>
/// The JSON Web Token an App authenticates with (GitHub: "Generating a JSON Web Token (JWT) for a GitHub App"): RS256, issued
/// a minute in the past to allow for clock drift, valid for nine minutes (GitHub accepts ten at most). It is built here, from the
/// framework's RSA, so that no library is added for one signature.
/// </summary>
internal static class GitHubAppJwt
{
    private static readonly TimeSpan Backdate = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(9);

    /// <summary>Creates the token, or returns null when <paramref name="privateKeyPem"/> is not a PEM-encoded RSA private key.</summary>
    public static string? TryCreate(long appId, string privateKeyPem, DateTimeOffset now)
    {
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(privateKeyPem);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return null;
        }

        try
        {
            var header = Encode(new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT" });
            var payload = Encode(new Dictionary<string, object>
            {
                ["iat"] = (now - Backdate).ToUnixTimeSeconds(),
                ["exp"] = (now + Lifetime).ToUnixTimeSeconds(),
                ["iss"] = appId,
            });
            var signed = $"{header}.{payload}";
            var signature = rsa.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return $"{signed}.{Base64Url(signature)}";
        }
        catch (CryptographicException)
        {
            // A key that imports but cannot sign (a public key, for one) is no credential.
            return null;
        }
    }

    private static string Encode(Dictionary<string, object> claims) => Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
