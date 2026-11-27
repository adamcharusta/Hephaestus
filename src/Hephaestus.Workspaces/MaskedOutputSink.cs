using System.Text;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;

namespace Hephaestus.Workspaces;

/// <summary>
/// Where the raw output of a container run goes on its way to <see cref="IOutputStore"/> (AD-18, AD-16): a stream to write bytes to, which decodes them as
/// UTF-8, passes the text through the masker, which finds a value that a chunk boundary splits, and writes what is safe to write. Output that is
/// not text is stored as the decoder reads it, with a replacement character for a byte that is not UTF-8. One instance serves one run and is not safe for concurrent use.
/// </summary>
internal sealed class MaskedOutputSink(IOutputWriter writer, SecretMasker masker) : Stream
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Decoder _decoder = Utf8.GetDecoder();
    private readonly SecretMaskingStream _masking = masker.CreateStream();
    private string? _tail;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var characters = new char[Utf8.GetMaxCharCount(buffer.Length)];
        var count = _decoder.GetChars(buffer.Span, characters, flush: false);
        await EmitAsync(_masking.Write(characters.AsSpan(0, count)), cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>
    /// Writes what the decoder and the masker still hold back, once no more output will come. What is held is taken from them once, so that a call that is made
    /// again, after a write that failed, writes the same text and a call that is made after it succeeded writes nothing.
    /// </summary>
    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        if (_tail is null)
        {
            var characters = new char[Utf8.GetMaxCharCount(0) + 1];
            var count = _decoder.GetChars([], characters, flush: true);
            _tail = _masking.Write(characters.AsSpan(0, count)) + _masking.Complete();
        }

        await EmitAsync(_tail, cancellationToken);
        _tail = string.Empty;
    }

    private async ValueTask EmitAsync(string text, CancellationToken cancellationToken)
    {
        if (text.Length > 0)
        {
            await writer.WriteAsync(Utf8.GetBytes(text), cancellationToken);
        }
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Output is written asynchronously.");
}
