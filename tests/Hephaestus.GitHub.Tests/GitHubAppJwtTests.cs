using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hephaestus.GitHub.Tests;

/// <summary>Story 1.7: the token an App authenticates with is RS256, backdated, short-lived, and signed with the App's key.</summary>
public class GitHubAppJwtTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static byte[] Decode(string part) => Convert.FromBase64String(part.Replace('-', '+').Replace('_', '/').PadRight((part.Length + 3) / 4 * 4, '='));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_token_is_signed_with_the_key_and_names_the_App(bool pkcs8)
    {
        using var rsa = RSA.Create(2048);
        var pem = pkcs8 ? rsa.ExportPkcs8PrivateKeyPem() : rsa.ExportRSAPrivateKeyPem();

        var jwt = GitHubAppJwt.TryCreate(123456, pem, Now);

        Assert.NotNull(jwt);
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Decode(parts[0]));
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
        using var payload = JsonDocument.Parse(Decode(parts[1]));
        Assert.Equal(123456, payload.RootElement.GetProperty("iss").GetInt64());
        Assert.Equal(Now.AddSeconds(-60).ToUnixTimeSeconds(), payload.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(Now.AddMinutes(9).ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a key")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nAAAA\n-----END RSA PRIVATE KEY-----")]
    public void Something_that_is_not_a_private_key_makes_no_token(string text)
    {
        Assert.Null(GitHubAppJwt.TryCreate(1, text, Now));
    }

    [Fact]
    public void A_public_key_makes_no_token()
    {
        using var rsa = RSA.Create(2048);

        Assert.Null(GitHubAppJwt.TryCreate(1, rsa.ExportSubjectPublicKeyInfoPem(), Now));
    }

    [Fact]
    public void A_key_of_another_kind_makes_no_token()
    {
        using var ecdsa = ECDsa.Create();

        Assert.Null(GitHubAppJwt.TryCreate(1, ecdsa.ExportPkcs8PrivateKeyPem(), Now));
    }
}
