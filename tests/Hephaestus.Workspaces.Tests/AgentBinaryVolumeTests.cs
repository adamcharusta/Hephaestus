using System.Net.Http;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Aliases;

namespace Hephaestus.Workspaces.Tests;

/// <summary>Story 3.1, AD-8, AR-40: the agent binaries volume is filled from the vendors, verified, and never left holding anything unverified.</summary>
public sealed class AgentBinaryVolumeTests : IDisposable
{
    private const AgentArchitecture Architecture = AgentArchitecture.X64;

    private readonly string _root = Directory.CreateTempSubdirectory("hx-agents").FullName;
    private readonly FakeVendor _vendor = new();
    private readonly HttpClient _http;

    public AgentBinaryVolumeTests() => _http = new HttpClient(_vendor.Handler);

    private string Volume => Path.Combine(_root, "agents");

    private string CaSource => Path.Combine(_root, "image-ca.crt");

    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private async Task<IReadOnlyList<AgentBinaryResult>> PrepareAsync(AgentArchitecture architecture = Architecture)
    {
        if (!File.Exists(CaSource))
        {
            await File.WriteAllTextAsync(CaSource, "certificates of the image", TestContext.Current.CancellationToken);
        }

        return await new AgentBinaryVolume(_http).PrepareAsync(_vendor.Lock, architecture, Volume, CaSource, TestContext.Current.CancellationToken);
    }

    private string PathOf(AgentBinary binary) => Path.Combine(Volume, binary.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static AgentBinary Pinned(AgentLock agentLock, string relativePath) => agentLock.Binaries.Single(b => b.RelativePath == relativePath);

    private AgentBinary Pinned(string relativePath) => Pinned(_vendor.Lock, relativePath);

    private AgentLock LockWith(AgentBinary replaced, string? sha256 = null, string? archiveSha256 = null)
    {
        var lines = _vendor.LockText.Split('\n').Select(line =>
        {
            var columns = line.Split(' ');
            if (columns.Length < 7 || columns[0] != replaced.Agent.Tag() || columns[2] != replaced.Architecture.Tag() || columns[3] != replaced.Libc.Tag() || columns[4] != replaced.File)
            {
                return line;
            }

            columns[5] = sha256 ?? columns[5];
            if (archiveSha256 is not null)
            {
                columns[7] = archiveSha256;
            }

            return string.Join(' ', columns);
        });
        return AgentLock.Parse(string.Join('\n', lines));
    }

    private static string Everything(string directory) => string.Join(
        "\n",
        Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/')).Order(StringComparer.Ordinal));

    [Fact]
    public async Task A_first_start_downloads_every_executable_of_the_architecture_from_its_vendors_address_and_verifies_each_digest()
    {
        var results = await PrepareAsync();

        Assert.All(results, result => Assert.True(result.Fetched));
        Assert.Equal(
            ["claude-code/x64-glibc/claude", "claude-code/x64-musl/claude", "codex-cli/x64-static/codex", "codex-cli/x64-static/codex-code-mode-host"],
            results.Select(result => result.Binary.RelativePath).Order(StringComparer.Ordinal));
        foreach (var result in results)
        {
            Assert.Equal(_vendor.Executable(result.Binary), await File.ReadAllBytesAsync(PathOf(result.Binary), TestContext.Current.CancellationToken));
        }

        Assert.Equal(_vendor.Lock.For(Architecture).Select(_vendor.Url).Order(StringComparer.Ordinal), _vendor.Requests.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(".staging", Directory.EnumerateFileSystemEntries(Volume).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Each_executable_is_reported_as_soon_as_it_is_verified()
    {
        var reported = new List<string>();

        await new AgentBinaryVolume(_http).PrepareAsync(_vendor.Lock, Architecture, Volume, WriteCa(), TestContext.Current.CancellationToken, result => reported.Add($"{result.Binary.RelativePath} {result.Fetched}"));

        Assert.Equal(4, reported.Count);
        Assert.All(reported, line => Assert.EndsWith("True", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_other_architectures_builds_are_not_downloaded()
    {
        await PrepareAsync(AgentArchitecture.Arm64);

        Assert.All(_vendor.Requests, url => Assert.Contains("/arm64-", url, StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(Volume, "claude-code", "x64-glibc")));
    }

    [Fact]
    public async Task A_volume_that_already_holds_the_pinned_verified_executables_downloads_nothing()
    {
        await PrepareAsync();
        _vendor.Requests.Clear();

        var second = await PrepareAsync();

        Assert.Empty(_vendor.Requests);
        Assert.All(second, result => Assert.False(result.Fetched));
    }

    [Fact]
    public async Task A_download_with_the_wrong_archive_digest_stops_startup_naming_the_executable_and_leaves_nothing_unverified()
    {
        var codex = Pinned("codex-cli/x64-static/codex");
        _vendor.Serve(codex, FakeVendor.Archive(("inside", [1, 2, 3])));

        var failure = await Assert.ThrowsAsync<AgentBinaryException>(() => PrepareAsync());

        Assert.Equal(MessageIds.AgentsStartChecksum, failure.Info.Id);
        Assert.Equal("codex-cli/x64-static/codex", failure.Info.Arguments[0]);
        Assert.Equal(codex.ArchiveSha256, failure.Info.Arguments[2]);
        Assert.False(File.Exists(PathOf(codex)));
        Assert.DoesNotContain(".staging", Everything(Volume), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_archive_that_matches_but_holds_an_executable_that_does_not_is_refused_with_the_executables_digest()
    {
        var codex = Pinned("codex-cli/x64-static/codex");
        var other = FakeVendor.Archive(("inside", [9, 9, 9]));
        _vendor.Serve(codex, other);
        var lockWithThatArchive = LockWith(codex, archiveSha256: FakeVendor.Sha(other));

        var failure = await Assert.ThrowsAsync<AgentBinaryException>(
            () => new AgentBinaryVolume(_http).PrepareAsync(lockWithThatArchive, Architecture, Volume, WriteCa(), TestContext.Current.CancellationToken));

        Assert.Equal(MessageIds.AgentsStartChecksum, failure.Info.Id);
        Assert.Equal(codex.Sha256, failure.Info.Arguments[2]);
        Assert.Equal(FakeVendor.Sha([9, 9, 9]), failure.Info.Arguments[3]);
        Assert.False(File.Exists(PathOf(codex)));
    }

    [Fact]
    public async Task A_claude_code_build_with_the_wrong_digest_is_refused_and_not_kept()
    {
        var musl = Pinned("claude-code/x64-musl/claude");
        _vendor.Serve(musl, [0xDE, 0xAD]);

        var failure = await Assert.ThrowsAsync<AgentBinaryException>(() => PrepareAsync());

        Assert.Equal(MessageIds.AgentsStartChecksum, failure.Info.Id);
        Assert.Equal("claude-code/x64-musl/claude", failure.Info.Arguments[0]);
        Assert.False(File.Exists(PathOf(musl)));
    }

    [Fact]
    public async Task A_file_that_is_there_with_the_wrong_digest_does_not_stay_when_its_replacement_cannot_be_fetched()
    {
        await PrepareAsync();
        var glibc = Pinned("claude-code/x64-glibc/claude");
        Tamper(PathOf(glibc), "tampered");
        _vendor.Withdraw(glibc);

        var failure = await Assert.ThrowsAsync<AgentBinaryException>(() => PrepareAsync());

        Assert.Equal(MessageIds.AgentsStartDownload, failure.Info.Id);
        Assert.False(File.Exists(PathOf(glibc)));
    }

    [Fact]
    public async Task A_file_with_the_wrong_digest_is_downloaded_again()
    {
        await PrepareAsync();
        var glibc = Pinned("claude-code/x64-glibc/claude");
        Tamper(PathOf(glibc), "tampered");
        _vendor.Requests.Clear();

        var results = await PrepareAsync();

        Assert.Equal([_vendor.Url(glibc)], _vendor.Requests);
        Assert.Equal([glibc.RelativePath], results.Where(result => result.Fetched).Select(result => result.Binary.RelativePath));
        Assert.Equal(_vendor.Executable(glibc), await File.ReadAllBytesAsync(PathOf(glibc), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_link_in_place_of_an_executable_is_not_the_executable()
    {
        await PrepareAsync();
        var glibc = Pinned("claude-code/x64-glibc/claude");
        var elsewhere = Path.Combine(_root, "elsewhere");
        await File.WriteAllBytesAsync(elsewhere, _vendor.Executable(glibc), TestContext.Current.CancellationToken);
        File.Delete(PathOf(glibc));
        File.CreateSymbolicLink(PathOf(glibc), elsewhere);
        _vendor.Requests.Clear();

        await PrepareAsync();

        Assert.Equal([_vendor.Url(glibc)], _vendor.Requests);
        Assert.Null(new FileInfo(PathOf(glibc)).LinkTarget);
    }

    [Fact]
    public async Task A_link_in_place_of_a_directory_on_the_way_to_an_executable_does_not_pass_for_the_executable()
    {
        await PrepareAsync();
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Directory.Move(Path.Combine(Volume, "claude-code", "x64-glibc"), Path.Combine(elsewhere, "x64-glibc"));
        Directory.Delete(Path.Combine(Volume, "claude-code"), recursive: true);
        Directory.CreateSymbolicLink(Path.Combine(Volume, "claude-code"), elsewhere);
        _vendor.Requests.Clear();

        var results = await PrepareAsync();

        Assert.Null(new FileInfo(Path.Combine(Volume, "claude-code")).LinkTarget);
        Assert.True(File.Exists(PathOf(Pinned("claude-code/x64-glibc/claude"))));
        Assert.Contains(results, result => result.Binary.RelativePath == "claude-code/x64-glibc/claude" && result.Fetched);
        Assert.Contains(_vendor.Url(Pinned("claude-code/x64-glibc/claude")), _vendor.Requests);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task A_file_with_the_digest_and_the_wrong_mode_gets_the_mode_a_step_container_needs()
    {
        await PrepareAsync();
        var glibc = Pinned("claude-code/x64-glibc/claude");
        File.SetUnixFileMode(PathOf(glibc), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        _vendor.Requests.Clear();

        await PrepareAsync();

        Assert.Empty(_vendor.Requests);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute, File.GetUnixFileMode(PathOf(glibc)));
    }

    [Fact]
    public async Task A_download_that_does_not_finish_in_time_stops_startup_naming_the_executable()
    {
        using var stalled = new HttpClient(new Stalled());

        var failure = await Assert.ThrowsAsync<AgentBinaryException>(
            () => new AgentBinaryVolume(stalled, TimeSpan.FromMilliseconds(50)).PrepareAsync(_vendor.Lock, Architecture, Volume, WriteCa(), TestContext.Current.CancellationToken));

        Assert.Equal(MessageIds.AgentsStartDownload, failure.Info.Id);
        Assert.Equal("timed out", failure.Info.Arguments[2]);
        Assert.DoesNotContain(".staging", Everything(Volume), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_download_names_the_executable_and_the_address()
    {
        var musl = Pinned("claude-code/x64-musl/claude");
        _vendor.Withdraw(musl);

        var failure = await Assert.ThrowsAsync<AgentBinaryException>(() => PrepareAsync());

        Assert.Equal(MessageIds.AgentsStartDownload, failure.Info.Id);
        Assert.Equal(["claude-code/x64-musl/claude", _vendor.Url(musl), "HTTP 404"], failure.Info.Arguments);
    }

    [Fact]
    public async Task A_vendor_that_cannot_be_reached_stops_startup()
    {
        _vendor.Unreachable = new HttpRequestException("no route to host");

        var failure = await Assert.ThrowsAsync<AgentBinaryException>(() => PrepareAsync());

        Assert.Equal(MessageIds.AgentsStartDownload, failure.Info.Id);
        Assert.Equal("no route to host", failure.Info.Arguments[2]);
    }

    [Fact]
    public async Task Cancelling_the_start_is_not_reported_as_a_failed_download()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new AgentBinaryVolume(_http).PrepareAsync(_vendor.Lock, Architecture, Volume, WriteCa(), cancelled.Token));
    }

    [Theory]
    [InlineData("two")]
    [InlineData("none")]
    [InlineData("not-gzip")]
    [InlineData("directory")]
    public async Task An_archive_that_does_not_hold_exactly_one_file_is_refused(string shape)
    {
        var codex = Pinned("codex-cli/x64-static/codex");
        var executable = _vendor.Executable(codex);
        var body = shape switch
        {
            "two" => FakeVendor.Archive(("a", executable), ("b", executable)),
            "none" => FakeVendor.Archive(),
            "directory" => DirectoryArchive(),
            _ => [1, 2, 3, 4],
        };
        _vendor.Serve(codex, body);

        var failure = await Assert.ThrowsAsync<AgentBinaryException>(
            () => new AgentBinaryVolume(_http).PrepareAsync(LockWith(codex, archiveSha256: FakeVendor.Sha(body)), Architecture, Volume, WriteCa(), TestContext.Current.CancellationToken));

        Assert.Equal(MessageIds.AgentsStartArchive, failure.Info.Id);
        Assert.Equal("codex-cli/x64-static/codex", failure.Info.Arguments[0]);
        Assert.False(File.Exists(PathOf(codex)));
    }

    [Fact]
    public async Task Everything_in_the_volume_that_the_lock_does_not_name_is_removed()
    {
        await PrepareAsync();
        Directory.CreateDirectory(Path.Combine(Volume, "old-agent", "x64-glibc"));
        await File.WriteAllTextAsync(Path.Combine(Volume, "old-agent", "x64-glibc", "old"), "x", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Volume, "claude-code", "x64-glibc", "claude.old"), "x", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Volume, "stray"), "x", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(Volume, "claude-code", "arm64-glibc"));
        File.CreateSymbolicLink(Path.Combine(Volume, "linked"), _root);

        await PrepareAsync();

        Assert.Equal(
            [
                "certs",
                "certs/ca-certificates.crt",
                "claude-code",
                "claude-code/x64-glibc",
                "claude-code/x64-glibc/claude",
                "claude-code/x64-musl",
                "claude-code/x64-musl/claude",
                "codex-cli",
                "codex-cli/x64-static",
                "codex-cli/x64-static/codex",
                "codex-cli/x64-static/codex-code-mode-host",
            ],
            Everything(Volume).Split('\n'));
    }

    [Fact]
    public async Task The_volume_holds_the_images_certificate_bundle_and_replaces_it_at_every_start()
    {
        await PrepareAsync();
        var bundle = Path.Combine(Volume, "certs", "ca-certificates.crt");
        Assert.Equal("certificates of the image", await File.ReadAllTextAsync(bundle, TestContext.Current.CancellationToken));

        Tamper(bundle, "planted");
        await File.WriteAllTextAsync(CaSource, "newer certificates", TestContext.Current.CancellationToken);
        await PrepareAsync();

        Assert.Equal("newer certificates", await File.ReadAllTextAsync(bundle, TestContext.Current.CancellationToken));
        Assert.Equal(["ca-certificates.crt"], Directory.EnumerateFileSystemEntries(Path.Combine(Volume, "certs")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_certificate_bundle_that_cannot_be_copied_stops_startup_naming_it()
    {
        var missing = Path.Combine(_root, "no-such-bundle.crt");

        var failure = await Assert.ThrowsAsync<AgentBinaryException>(
            () => new AgentBinaryVolume(_http).PrepareAsync(_vendor.Lock, Architecture, Volume, missing, TestContext.Current.CancellationToken));

        Assert.Equal(MessageIds.AgentsStartCaBundle, failure.Info.Id);
        Assert.Equal(missing, failure.Info.Arguments[0]);
    }

    [Fact]
    public async Task A_volume_that_cannot_be_written_stops_startup_naming_it()
    {
        await File.WriteAllTextAsync(Volume, "a file where the directory should be", TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<AgentBinaryException>(() => PrepareAsync());

        Assert.Equal(MessageIds.AgentsStartVolume, failure.Info.Id);
        Assert.Equal(Volume, failure.Info.Arguments[0]);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Executables_can_be_read_and_run_by_any_user_and_written_by_none()
    {
        var results = await PrepareAsync();

        Assert.All(results, result => Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute, File.GetUnixFileMode(PathOf(result.Binary))));
        Assert.True(File.GetUnixFileMode(Path.Combine(Volume, "certs", "ca-certificates.crt")).HasFlag(UnixFileMode.OtherRead));
        Assert.True(File.GetUnixFileMode(Path.Combine(Volume, "claude-code", "x64-glibc")).HasFlag(UnixFileMode.OtherExecute));
    }

    /// <summary>A change by someone who may write where the host may not: the files are read-only to everyone.</summary>
    private static void Tamper(string path, string content)
    {
        File.Delete(path);
        File.WriteAllText(path, content);
    }

    private string WriteCa()
    {
        File.WriteAllText(CaSource, "certificates of the image");
        return CaSource;
    }

    private static byte[] DirectoryArchive()
    {
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        using (var tar = new System.Formats.Tar.TarWriter(gzip, System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true))
        {
            tar.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.Directory, "a-directory"));
        }

        return output.ToArray();
    }

    private sealed class Stalled : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Not reached.");
        }
    }
}
