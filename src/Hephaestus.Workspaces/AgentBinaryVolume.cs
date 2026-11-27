using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;

namespace Hephaestus.Workspaces;

/// <summary>What the volume held for one pinned executable once it was prepared.</summary>
public sealed record AgentBinaryResult(AgentBinary Binary, bool Fetched);

/// <summary>
/// The agent binaries volume, as the host sees it (AD-8, Story 3.1). It brings the volume to the state the lock demands: every executable
/// the lock names for this architecture is present and has its digest, nothing else is in it but the CA certificate bundle, and
/// a download that did not match is never left behind. An executable that is already there with its digest is not downloaded again.
/// A download goes to a staging directory inside the volume and is moved into place, in one rename, only after its digest matched, so a
/// failure leaves nothing unverified.
/// </summary>
public sealed class AgentBinaryVolume(HttpClient http, TimeSpan? downloadLimit = null)
{
    public const string CaBundleRelativePath = "certs/ca-certificates.crt";

    private const string StagingDirectory = ".staging";
    private static readonly TimeSpan DefaultDownloadLimit = TimeSpan.FromMinutes(30);
    private static readonly UnixFileMode ExecutableMode = UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>
    /// Fetches what is missing or wrong, verifies every executable, removes what the lock does not name, and replaces the CA bundle.
    /// <paramref name="ready"/> is called as each executable is verified, so that a first start, which takes minutes, says where it is.
    /// </summary>
    /// <exception cref="AgentBinaryException">A download failed or did not match, or the volume cannot be written.</exception>
    public async Task<IReadOnlyList<AgentBinaryResult>> PrepareAsync(
        AgentLock agentLock,
        AgentArchitecture architecture,
        string directory,
        string caBundleSource,
        CancellationToken cancellationToken,
        Action<AgentBinaryResult>? ready = null)
    {
        var results = new List<AgentBinaryResult>();
        try
        {
            CreateDirectory(directory);
            DeleteTree(Path.Combine(directory, StagingDirectory));

            // What the lock does not name goes first, so that a link in place of a directory on the way to an executable cannot make
            // a file outside the volume look like a pinned one.
            var binaries = agentLock.For(architecture);
            Prune(directory, [.. binaries.Select(binary => binary.RelativePath), CaBundleRelativePath]);
            foreach (var binary in binaries)
            {
                var result = new AgentBinaryResult(binary, await EnsureAsync(binary, directory, cancellationToken));
                results.Add(result);
                ready?.Invoke(result);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AgentBinaryException(new Message(MessageIds.AgentsStartVolume, directory, exception.Message), exception);
        }
        finally
        {
            DeleteTree(Path.Combine(directory, StagingDirectory));
        }

        PlaceCaBundle(caBundleSource, directory);
        return results;
    }

    private async Task<bool> EnsureAsync(AgentBinary binary, string directory, CancellationToken cancellationToken)
    {
        var path = PathOf(directory, binary);
        if (await MatchesAsync(path, binary.Sha256, cancellationToken))
        {
            // A volume that was restored or touched by hand keeps the digest and may have lost the mode a step container needs.
            if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != ExecutableMode)
            {
                File.SetUnixFileMode(path, ExecutableMode);
            }

            return false;
        }

        // Whatever is there is not what the lock pins; it must not stay while its replacement is fetched.
        DeleteTree(path);
        var staging = Path.Combine(directory, StagingDirectory, Guid.NewGuid().ToString("N"));
        CreateDirectory(staging);
        var executable = await FetchAsync(binary, staging, cancellationToken);

        CreateDirectory(Path.GetDirectoryName(path)!);
        File.Move(executable, path, overwrite: true);
        if (!await MatchesAsync(path, binary.Sha256, cancellationToken))
        {
            DeleteTree(path);
            throw new AgentBinaryException(new Message(MessageIds.AgentsStartUnverified, binary.RelativePath));
        }

        return true;
    }

    /// <summary>Downloads and verifies, and returns the path of the verified executable inside the staging directory.</summary>
    private async Task<string> FetchAsync(AgentBinary binary, string staging, CancellationToken cancellationToken)
    {
        var download = Path.Combine(staging, "download");
        var expected = binary.ArchiveSha256 ?? binary.Sha256;
        var actual = await DownloadAsync(binary, download, cancellationToken);
        if (actual != expected)
        {
            throw Mismatch(binary, expected, actual);
        }

        var executable = Path.Combine(staging, "executable");
        if (binary.IsArchive)
        {
            actual = await ExtractAsync(binary, download, executable, cancellationToken);
            if (actual != binary.Sha256)
            {
                throw Mismatch(binary, binary.Sha256, actual);
            }
        }
        else
        {
            File.Move(download, executable);
        }

        if (!OperatingSystem.IsWindows())
        {
            // Read and execute for every user, because a step container runs the executable as a user of its own; write for no one.
            File.SetUnixFileMode(executable, ExecutableMode);
        }

        return executable;
    }

    private async Task<string> DownloadAsync(AgentBinary binary, string target, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(downloadLimit ?? DefaultDownloadLimit);
        try
        {
            using var response = await http.GetAsync(binary.Url, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new AgentBinaryException(new Message(MessageIds.AgentsStartDownload, binary.RelativePath, binary.Url.AbsoluteUri, $"HTTP {(int)response.StatusCode}"));
            }

            await using var source = await response.Content.ReadAsStreamAsync(limit.Token);
            await using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            return await CopyHashedAsync(source, file, limit.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            var reason = exception is OperationCanceledException ? "timed out" : exception.Message;
            throw new AgentBinaryException(new Message(MessageIds.AgentsStartDownload, binary.RelativePath, binary.Url.AbsoluteUri, reason), exception);
        }
    }

    /// <summary>Takes the one file out of the archive; an archive with anything else in it is refused.</summary>
    private static async Task<string> ExtractAsync(AgentBinary binary, string archive, string target, CancellationToken cancellationToken)
    {
        try
        {
            await using var compressed = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var tar = new GZipStream(compressed, CompressionMode.Decompress);
            await using var reader = new TarReader(tar);
            var entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken);
            if (entry is not { EntryType: TarEntryType.RegularFile or TarEntryType.V7RegularFile, DataStream: { } data })
            {
                throw Unexpected(binary);
            }

            string digest;
            await using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                digest = await CopyHashedAsync(data, file, cancellationToken);
            }

            if (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is not null)
            {
                throw Unexpected(binary);
            }

            return digest;
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            // A full disk is an IOException of the volume, not a fault of the archive; this is only an archive that cannot be read.
            throw Unexpected(binary, exception);
        }
    }

    private static async Task<string> CopyHashedAsync(Stream source, Stream target, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static async Task<bool> MatchesAsync(string path, string sha256, CancellationToken cancellationToken)
    {
        // A link is not a file this host placed, whatever it points at.
        if (!File.Exists(path) || new FileInfo(path).LinkTarget is not null)
        {
            return false;
        }

        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken)) == sha256;
    }

    /// <summary>Removes everything in the volume except the files named and the directories that hold them.</summary>
    private static void Prune(string directory, IReadOnlyCollection<string> keep)
    {
        var kept = keep.ToHashSet(StringComparer.Ordinal);
        var parents = kept.SelectMany(path => Ancestors(path)).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0 }))
        {
            Sweep(directory, entry, kept, parents);
        }
    }

    private static void Sweep(string root, string entry, HashSet<string> kept, HashSet<string> parents)
    {
        var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
        var isLink = new FileInfo(entry).LinkTarget is not null;
        if (Directory.Exists(entry) && !isLink)
        {
            if (!parents.Contains(relative))
            {
                Directory.Delete(entry, recursive: true);
                return;
            }

            foreach (var child in Directory.EnumerateFileSystemEntries(entry, "*", new EnumerationOptions { AttributesToSkip = 0 }))
            {
                Sweep(root, child, kept, parents);
            }

            return;
        }

        if (isLink || !kept.Contains(relative))
        {
            DeleteTree(entry);
        }
    }

    private static IEnumerable<string> Ancestors(string relative)
    {
        for (var slash = relative.LastIndexOf('/'); slash > 0; slash = relative.LastIndexOf('/', slash - 1))
        {
            yield return relative[..slash];
        }
    }

    /// <summary>
    /// Copies the image's own certificate bundle over the volume's, at every start, through a temporary file so that a step container
    /// never reads half of one.
    /// </summary>
    private static void PlaceCaBundle(string source, string directory)
    {
        var target = Path.Combine(directory, CaBundleRelativePath);
        try
        {
            CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".ca-certificates.crt.tmp");
            File.Copy(source, temporary, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            File.Move(temporary, target, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AgentBinaryException(new Message(MessageIds.AgentsStartCaBundle, source, exception.Message), exception);
        }
    }

    private static string PathOf(string directory, AgentBinary binary) => Path.Combine(directory, binary.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A directory every user can read and enter, because a step container reads it as a user of its own.</summary>
    private static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static void DeleteTree(string path)
    {
        if (Directory.Exists(path) && new FileInfo(path).LinkTarget is null)
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path) || new FileInfo(path).LinkTarget is not null)
        {
            File.Delete(path);
        }
    }

    private static AgentBinaryException Mismatch(AgentBinary binary, string expected, string actual) =>
        new(new Message(MessageIds.AgentsStartChecksum, binary.RelativePath, binary.Url.AbsoluteUri, expected, actual));

    private static AgentBinaryException Unexpected(AgentBinary binary, Exception? inner = null) =>
        new(new Message(MessageIds.AgentsStartArchive, binary.RelativePath, binary.Url.AbsoluteUri), inner);
}
