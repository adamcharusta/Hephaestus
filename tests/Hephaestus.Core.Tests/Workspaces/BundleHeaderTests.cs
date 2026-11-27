using System.Text;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

/// <summary>Story 3.6, AD-9: the header of a bundle that came out of a container is read as text, and anything that is not a header is unreadable and not guessed at.</summary>
public class BundleHeaderTests
{
    private static readonly string Parent = new('a', 40);
    private static readonly string Tip = new('b', 40);

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void A_version_two_header_gives_its_heads_and_its_prerequisites()
    {
        var header = BundleHeader.TryRead(Bytes($"# v2 git bundle\n-{Parent} first commit\n{Tip} refs/heads/hephaestus/12-x\n\nPACKdata"));

        Assert.NotNull(header);
        Assert.Equal([(Tip, "refs/heads/hephaestus/12-x")], header.Heads);
        Assert.Equal([Parent], header.Prerequisites);
    }

    [Fact]
    public void A_version_three_header_with_a_capability_line_is_read_too()
    {
        var header = BundleHeader.TryRead(Bytes($"# v3 git bundle\n@object-format=sha1\n-{Parent}\n{Tip} refs/heads/x\n\nPACK"));

        Assert.NotNull(header);
        Assert.Equal([(Tip, "refs/heads/x")], header.Heads);
        Assert.Equal([Parent], header.Prerequisites);
    }

    [Fact]
    public void A_bundle_that_needs_nothing_has_no_prerequisite()
    {
        var header = BundleHeader.TryRead(Bytes($"# v2 git bundle\n{Tip} refs/heads/x\n\nPACK"));

        Assert.NotNull(header);
        Assert.Empty(header.Prerequisites);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a bundle")]
    [InlineData("# v1 git bundle\n\n")]
    [InlineData("# v2 git bundle\n")]
    [InlineData("# v2 git bundle\nnot a header line\n\nPACK")]
    [InlineData("# v2 git bundle\n-short\n\nPACK")]
    [InlineData("# v2 git bundle\n-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaX\n\nPACK")]
    [InlineData("# v2 git bundle\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n\nPACK")]
    [InlineData("# v2 git bundle\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb \n\nPACK")]
    [InlineData("# v2 git bundle\n@object-format=sha1\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb refs/heads/x\n\nPACK")]
    [InlineData("# v2 git bundle\nBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB refs/heads/x\n\nPACK")]
    [InlineData("# v2 git bundle\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb refs/heads/x\r\n\nPACK")]
    public void Anything_that_is_not_a_header_is_not_read(string text)
    {
        Assert.Null(BundleHeader.TryRead(Bytes(text)));
    }

    [Fact]
    public void A_prerequisite_whose_subject_is_in_another_encoding_or_very_long_is_still_read()
    {
        byte[] legacy = [.. "# v2 git bundle\n-"u8, .. Encoding.ASCII.GetBytes(Parent), .. " Zażółć gęślą jaźń "u8, 0xB1, 0xE6, 0xEA, (byte)'\n', .. Encoding.ASCII.GetBytes(Tip), .. " refs/heads/x\n\nPACK"u8];
        var longSubject = Bytes($"# v2 git bundle\n-{Parent} {new string('s', 50_000)}\n{Tip} refs/heads/x\n\nPACK");

        foreach (var header in new[] { BundleHeader.TryRead(new MemoryStream(legacy)), BundleHeader.TryRead(longSubject) })
        {
            Assert.NotNull(header);
            Assert.Equal([Parent], header.Prerequisites);
            Assert.Equal([(Tip, "refs/heads/x")], header.Heads);
        }
    }

    [Fact]
    public void A_reference_name_that_is_not_text_is_not_read()
    {
        byte[] bytes = [.. "# v2 git bundle\n"u8, .. Encoding.ASCII.GetBytes(Tip), .. " refs/heads/"u8, 0xC3, 0x28, .. "\n\nPACK"u8];

        Assert.Null(BundleHeader.TryRead(new MemoryStream(bytes)));
    }

    [Fact]
    public void Bytes_that_are_not_text_and_a_header_with_no_end_within_the_bound_are_not_read()
    {
        Assert.Null(BundleHeader.TryRead(new MemoryStream([.. "# v2 git bundle\n"u8, 0xC3, 0x28, .. "\n\nPACK"u8])));
        Assert.Null(BundleHeader.TryRead(Bytes("# v2 git bundle\n" + new string('x', BundleHeader.MaximumBytes))));
    }

    [Fact]
    public void A_stream_that_gives_its_bytes_in_pieces_is_read_whole()
    {
        var header = BundleHeader.TryRead(new TrickleStream(Encoding.UTF8.GetBytes($"# v2 git bundle\n-{Parent} c\n{Tip} refs/heads/x\n\nPACK")));

        Assert.NotNull(header);
        Assert.Equal([(Tip, "refs/heads/x")], header.Heads);
    }

    [Fact]
    public void A_stream_is_needed()
    {
        Assert.Throws<ArgumentNullException>(() => BundleHeader.TryRead(null!));
    }

    private sealed class TrickleStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 7));
    }
}
