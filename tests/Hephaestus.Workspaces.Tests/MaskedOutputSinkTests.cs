using System.Text;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using NSubstitute;

namespace Hephaestus.Workspaces.Tests;

/// <summary>Story 3.3, AD-18, AD-16: the stream that raw output is written to decodes it as UTF-8, masks it across chunk boundaries, and writes it to the store.</summary>
public class MaskedOutputSinkTests
{
    private const string Secret = "sk-test-0123456789-abcdefghij";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(MaskedOutputSink Sink, InMemoryOutput.Buffer File)> OpenAsync(params string[] secrets)
    {
        var store = Substitute.For<ISecretStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(secrets);
        var masker = new SecretMasker(store);
        await masker.RebuildAsync(Token);
        var output = new InMemoryOutput([]);
        var writer = await output.BeginAsync(new Hephaestus.Core.Output.OutputRun(Guid.Empty, Guid.Empty, "c"), Token);
        return (new MaskedOutputSink(writer, masker), output.Files[Guid.Empty]);
    }

    [Fact]
    public async Task A_character_whose_bytes_arrive_in_two_chunks_is_decoded_whole()
    {
        var (sink, file) = await OpenAsync();
        var bytes = Encoding.UTF8.GetBytes("zażółć gęślą jaźń\n");

        await sink.WriteAsync(bytes.AsMemory(0, 4), Token);
        await sink.WriteAsync(bytes.AsMemory(4), Token);
        await sink.CompleteAsync(Token);

        Assert.Equal("zażółć gęślą jaźń\n", file.Text);
    }

    [Fact]
    public async Task A_byte_that_is_not_UTF_8_is_stored_as_the_replacement_character_and_the_rest_is_kept()
    {
        var (sink, file) = await OpenAsync();

        await sink.WriteAsync(new byte[] { (byte)'a', 0xFF, (byte)'b' }, Token);
        await sink.CompleteAsync(Token);

        Assert.Equal("a�b", file.Text);
    }

    [Fact]
    public async Task The_start_of_a_character_that_never_ends_is_written_as_the_replacement_character_when_the_output_ends()
    {
        var (sink, file) = await OpenAsync();

        await sink.WriteAsync(new byte[] { (byte)'x', 0xC5 }, Token);
        await sink.CompleteAsync(Token);

        Assert.Equal("x�", file.Text);
    }

    [Fact]
    public async Task A_secret_split_across_chunks_is_masked_whole_and_what_follows_is_kept()
    {
        var (sink, file) = await OpenAsync(Secret);

        await sink.WriteAsync(Encoding.UTF8.GetBytes("token sk-test-01234"), Token);
        await sink.WriteAsync(Encoding.UTF8.GetBytes("56789-abcdefghij end"), Token);
        await sink.CompleteAsync(Token);

        Assert.Equal("token *** end", file.Text);
    }

    [Fact]
    public async Task A_secret_that_begins_the_output_and_is_not_finished_when_it_ends_is_not_held_back_forever_nor_written_as_a_secret()
    {
        var (sink, file) = await OpenAsync(Secret);

        await sink.WriteAsync(Encoding.UTF8.GetBytes("sk-test-0123"), Token);
        await sink.CompleteAsync(Token);

        Assert.Equal("sk-test-0123", file.Text);
    }

    [Fact]
    public async Task Nothing_is_written_for_output_that_has_none()
    {
        var (sink, file) = await OpenAsync();

        await sink.WriteAsync(ReadOnlyMemory<byte>.Empty, Token);
        await sink.CompleteAsync(Token);

        Assert.Equal(0, file.Bytes.Length);
    }

    [Fact]
    public async Task The_array_overload_of_a_stream_write_goes_the_same_way()
    {
        var (sink, file) = await OpenAsync();
        var bytes = Encoding.UTF8.GetBytes("--abc--");

        await sink.WriteAsync(bytes, 2, 3, Token);
        await sink.FlushAsync(Token);
        sink.Flush();
        await sink.CompleteAsync(Token);

        Assert.Equal("abc", file.Text);
    }

    [Fact]
    public async Task The_sink_is_a_stream_that_can_only_be_written_to_asynchronously()
    {
        var (sink, _) = await OpenAsync();

        Assert.True(sink.CanWrite);
        Assert.False(sink.CanRead);
        Assert.False(sink.CanSeek);
        Assert.Throws<NotSupportedException>(() => sink.Length);
        Assert.Throws<NotSupportedException>(() => sink.Position);
        Assert.Throws<NotSupportedException>(() => sink.Position = 1);
        Assert.Throws<NotSupportedException>(() => sink.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => sink.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => sink.SetLength(0));
        Assert.Throws<NotSupportedException>(() => sink.Write(new byte[1], 0, 1));
    }

    [Fact]
    public async Task Completing_again_after_a_write_failed_writes_what_was_held_back_once_and_completing_after_that_writes_nothing()
    {
        var store = Substitute.For<ISecretStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns([Secret]);
        var masker = new SecretMasker(store);
        await masker.RebuildAsync(Token);
        var writer = new FlakyWriter();
        var sink = new MaskedOutputSink(writer, masker);
        await sink.WriteAsync(Encoding.UTF8.GetBytes("end of output sk-test-01234"), Token);

        await Assert.ThrowsAsync<IOException>(() => sink.CompleteAsync(Token));
        await sink.CompleteAsync(Token);
        await sink.CompleteAsync(Token);

        Assert.Equal("end of output sk-test-01234", writer.Text);
    }

    private sealed class FlakyWriter : IOutputWriter
    {
        private readonly MemoryStream _bytes = new();
        private bool _failed;

        public string Text => Encoding.UTF8.GetString(_bytes.ToArray());

        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            if (!_failed && bytes.Length > 0 && Encoding.UTF8.GetString(bytes.Span).Contains("sk-test-01234", StringComparison.Ordinal))
            {
                _failed = true;
                throw new IOException("disk is full");
            }

            _bytes.Write(bytes.Span);
            return ValueTask.CompletedTask;
        }

        public Task<Hephaestus.Core.Output.StoredOutput> CompleteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
