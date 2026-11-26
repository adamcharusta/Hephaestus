using Hephaestus.Core.Secrets;

namespace Hephaestus.Core.Tests.Secrets;

/// <summary>Story 1.7, finding 13 of Story 1.6: a stored PEM private key is masked whole and one line at a time.</summary>
public class MultiLineSecretTests
{
    private const string Mask = SecretMasker.Replacement;
    private const string Header = "-----BEGIN RSA PRIVATE KEY-----";
    private const string Footer = "-----END RSA PRIVATE KEY-----";
    private const string First = "MIIEowIBAAKCAQEAu1SU1LfVLPHCozMxH2Mo4lgOEePzNm0tRgeLezV6ffAt0gun";
    private const string Second = "VTyCwoJwZLNRvWqDa4YKvxi9KBMlRVBXPqMh8t3cgZR2qpbkSvLV1NhMXs7FlQz4";
    private const string Last = "AbCdEfGhIjKl";

    private static string Pem(string lineBreak) => string.Join(lineBreak, Header, First, Second, Last, Footer) + lineBreak;

    private static async Task<SecretMasker> BuiltOverAsync(string value)
    {
        var masker = new SecretMasker(new FakeSecretStore(value));
        await masker.RebuildAsync(TestContext.Current.CancellationToken);
        return masker;
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Each_line_of_the_key_is_masked_when_a_tool_logs_it_on_its_own(string lineBreak)
    {
        var masker = await BuiltOverAsync(Pem(lineBreak));

        Assert.Equal($"key line: {Mask}", masker.Mask($"key line: {First}"));
        Assert.Equal(Mask, masker.Mask(Second));
        Assert.Equal(Mask, masker.Mask(Last));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task The_whole_key_is_masked_as_one_run(string lineBreak)
    {
        var masker = await BuiltOverAsync(Pem(lineBreak));

        Assert.Equal($"before {Mask} after", masker.Mask($"before {Pem(lineBreak).TrimEnd('\r', '\n')} after"));
    }

    [Fact]
    public async Task The_lines_that_frame_the_key_are_not_secret_and_stay_readable()
    {
        var masker = await BuiltOverAsync(Pem("\n"));

        Assert.Equal(Header, masker.Mask(Header));
        Assert.Equal(Footer, masker.Mask(Footer));
    }

    [Fact]
    public async Task A_line_that_is_too_short_to_be_told_from_ordinary_text_is_not_masked()
    {
        var masker = await BuiltOverAsync(string.Join('\n', Header, First, "AQAB", Footer));

        Assert.Equal("AQAB", masker.Mask("AQAB"));
        Assert.Equal(Mask, masker.Mask(First));
    }

    [Fact]
    public async Task A_line_is_masked_across_a_chunk_boundary()
    {
        var masker = await BuiltOverAsync(Pem("\n"));
        var stream = masker.CreateStream();

        var half = First.Length / 2;
        var masked = stream.Write($"line {First[..half]}") + stream.Write(First[half..] + " end") + stream.Complete();

        Assert.Equal($"line {Mask} end", masked);
    }
}
