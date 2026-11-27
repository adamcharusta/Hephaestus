using System.Formats.Tar;
using System.Security.Cryptography;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces;

/// <summary>The tar archives the runtime sends to a container that is not running (Docker's copy-to-container).</summary>
internal static class TarFiles
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>
    /// The files, and the directories above them, owned by root and readable by every user and writable by none: a step container runs as a
    /// user of its own and mounts them read-only.
    /// </summary>
    public static MemoryStream ReadOnlyFiles(IEnumerable<WorkspaceFile> files)
    {
        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            var directories = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var names = file.Path.Split('/');
                for (var depth = 1; depth < names.Length; depth++)
                {
                    directories.Add(string.Join('/', names.Take(depth)));
                }
            }

            foreach (var directory in directories)
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, directory) { Mode = DirectoryMode, Uid = 0, Gid = 0 });
            }

            foreach (var file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, file.Path) { Mode = FileMode, Uid = 0, Gid = 0, DataStream = new MemoryStream(file.Content) });
            }
        }

        archive.Position = 0;
        return archive;
    }

    /// <summary>
    /// One directory entry, <paramref name="directory"/>, owned by <paramref name="userId"/> and <paramref name="groupId"/>. Extracted onto the
    /// mount point of a volume it sets the owner of the volume's root, which Docker gives to root when it creates the volume.
    /// </summary>
    public static MemoryStream OwnedDirectory(string directory, int userId, int groupId)
    {
        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, directory)
            {
                Mode = DirectoryMode | UnixFileMode.UserWrite,
                Uid = userId,
                Gid = groupId,
            });
        }

        archive.Position = 0;
        return archive;
    }

    /// <summary>
    /// A directory and the one file in it, both owned by <paramref name="userId"/> and <paramref name="groupId"/> and readable by that user alone, written to a temporary file that is deleted when the
    /// stream is closed, so that a bundle of any size is never held in memory. The SHA-256 of the file is taken while it is copied, so that the bytes that are checked are the bytes that are sent.
    /// Extracted onto the root of a container that has not started, it is where the checkout reads the bundle from.
    /// </summary>
    public static (Stream Archive, string Sha256) OwnedFile(string directory, string name, string source, string temporaryPath, int userId, int groupId)
    {
        var archive = new FileStream(temporaryPath, System.IO.FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, directory) { Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, Uid = userId, Gid = groupId });
                using var file = new HashingReadStream(new FileStream(source, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan), hash);
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{directory}/{name}") { Mode = UnixFileMode.UserRead, Uid = userId, Gid = groupId, DataStream = file });
            }

            archive.Position = 0;
            return (archive, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>What reading one file out of an archive found (AD-9): the file, written to <c>destination</c>, with its SHA-256 and size, or why nothing was.</summary>
    public enum ReadOutcome
    {
        Written,

        /// <summary>The archive is not exactly one regular file of the expected name: a link, a directory, a path with a parent or a prefix, another name, an extra entry, or a stream that is not a tar.</summary>
        NotOneRegularFile,

        /// <summary>The file is larger than the bound; nothing was written.</summary>
        TooLarge,
    }

    /// <summary>
    /// Reads the one file a container returns from a stopped container's archive (Docker's copy-from-container): the first entry must be a regular file called exactly <paramref name="name"/>, no larger than
    /// <paramref name="maximumBytes"/>, and there must be no second entry. A link entry, a name with a parent or a directory in it, or anything else makes it <see cref="ReadOutcome.NotOneRegularFile"/>, and a file that
    /// was started is deleted: nothing from such an archive is used. It is read asynchronously, because the stream of a copy out of a container does not support a synchronous read, and the copy stops at <paramref name="cancellationToken"/>. The size is the header's and the copy is bounded by it, so a stream that is longer than it says is never read past it.
    /// </summary>
    public static async Task<(ReadOutcome Outcome, string? Sha256, long Length)> ReadOneFileAsync(Stream archive, string name, long maximumBytes, string destination, CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = new TarReader(archive, leaveOpen: true);
            var entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken);
            if (entry is not { EntryType: TarEntryType.RegularFile or TarEntryType.V7RegularFile, DataStream: { } data } || !string.Equals(entry.Name, name, StringComparison.Ordinal))
            {
                return (ReadOutcome.NotOneRegularFile, null, 0);
            }

            if (entry.Length > maximumBytes)
            {
                return (ReadOutcome.TooLarge, null, entry.Length);
            }

            string checksum;
            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var file = new FileStream(destination, System.IO.FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.SequentialScan | FileOptions.Asynchronous))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await data.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }

                // Whatever follows the file is an extra entry, unless it is the end of the archive.
                if (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is not null)
                {
                    File.Delete(destination);
                    return (ReadOutcome.NotOneRegularFile, null, 0);
                }

                checksum = Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            catch
            {
                File.Delete(destination);
                throw;
            }

            return (ReadOutcome.Written, checksum, entry.Length);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or FormatException)
        {
            return (ReadOutcome.NotOneRegularFile, null, 0);
        }
    }

    /// <summary>A stream that feeds everything read through it to a hash.</summary>
    private sealed class HashingReadStream(Stream inner, IncrementalHash hash) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            hash.AppendData(buffer, offset, read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            hash.AppendData(buffer[..read]);
            return read;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
