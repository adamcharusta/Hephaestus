using System.Text;
using Hephaestus.Core.Secrets;

namespace Hephaestus.Core.Tests.Secrets;

/// <summary>AD-9, AD-16, AR-52, Story 3.7: a stored value is found in plain, base64, and URL-encoded form, in text that arrives in chunks, and in no other text.</summary>
public class SecretScanTests
{
    private const string Value = "sk-test-0123456789-abcdefghij";

    private static async Task<SecretMasker> BuiltOverAsync(params string[] values)
    {
        var masker = new SecretMasker(new FakeSecretStore(values));
        await masker.RebuildAsync(TestContext.Current.CancellationToken);
        return masker;
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("base64")]
    [InlineData("base64-unpadded")]
    [InlineData("url")]
    public async Task A_value_is_found_in_each_form_the_masker_knows(string form)
    {
        const string Awkward = "p@ss word/with+symbols=1";
        var masker = await BuiltOverAsync(Awkward);
        var text = form switch
        {
            "plain" => Awkward,
            "base64" => Convert.ToBase64String(Encoding.UTF8.GetBytes(Awkward)),
            "base64-unpadded" => Convert.ToBase64String(Encoding.UTF8.GetBytes(Awkward)).TrimEnd('='),
            _ => Uri.EscapeDataString(Awkward),
        };

        var scan = masker.CreateScan();

        Assert.True(scan.Write($"token = \"{text}\"\n"));
        Assert.True(scan.Found);
    }

    [Fact]
    public async Task A_value_that_a_chunk_boundary_splits_is_found_and_text_without_it_is_not()
    {
        var masker = await BuiltOverAsync(Value);
        var text = "before " + Value + " after";

        for (var split = 1; split < text.Length; split++)
        {
            var scan = masker.CreateScan();
            scan.Write(text.AsSpan(0, split));
            scan.Write(text.AsSpan(split));
            Assert.True(scan.Found, $"split at {split}");
        }

        var clean = masker.CreateScan();
        foreach (var chunk in "nothing to see here, sk-test-0123456789-abcdefgh and so on".Chunk(5))
        {
            Assert.False(clean.Write(chunk));
        }

        Assert.False(clean.Found);
    }

    [Fact]
    public async Task A_masker_with_no_secret_finds_nothing_and_an_empty_text_holds_none()
    {
        var masker = await BuiltOverAsync();

        Assert.False(masker.CreateScan().Write("anything"));
    }

    [Fact]
    public async Task A_scan_that_found_a_value_keeps_saying_so()
    {
        var masker = await BuiltOverAsync(Value);
        var scan = masker.CreateScan();

        Assert.True(scan.Write(Value));
        Assert.True(scan.Write("more"));
        Assert.True(scan.Found);
    }
}
