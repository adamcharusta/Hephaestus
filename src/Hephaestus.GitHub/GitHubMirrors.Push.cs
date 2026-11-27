using System.Security.Cryptography;
using System.Text;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Protection;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.GitHub;

/// <summary>
/// Pushing a Task branch (AD-4, AD-9, AD-19, FR-4, FR-20, FR-44). The only code that pushes is here. A push is a fast-forward of one branch, <c>refs/heads/hephaestus/...</c>, from a quarantine ref of the mirror, and there is no
/// other form of it: no plus in the refspec, no force, no delete, no other ref, because the request cannot name one (<see cref="PushRequest"/>) and the refspec is built from the Task branch and a quarantine name alone. The checks run in the
/// order of the story, and the first that fails blocks the push: the bundle's file, where the branch is on GitHub, the bundle's heads, the import with strict object checking, the fast-forward, the secrets, and the protected paths.
/// </summary>
public sealed partial class GitHubMirrors
{
    /// <summary>
    /// The one command that reads a file of the data volume names the <c>file</c> protocol, because git counts a bundle on disk as one; it reads nothing else, and what it is given was verified as a regular file with the checksum the runtime wrote,
    /// and as a bundle with one head. Every command that talks to GitHub has only the protocols of <see cref="GitMirrorOptions.AllowedProtocols"/>.
    /// </summary>
    private static readonly Dictionary<string, string> LocalFile = new() { ["GIT_ALLOW_PROTOCOL"] = "file" };

    private const string QuarantinePrefix = "refs/hephaestus/quarantine/";
    private const int MaximumObjects = 1_000_000;
    private const int MaximumFindings = 5;
    private const int CommitsShown = 10;
    private const int AbbreviatedLength = 12;

    public async Task<BranchCheckResult> CheckBranchAsync(BranchCheckRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var gate = _gates.GetOrAdd(request.RepositoryId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (await FindProjectAsync(request.RepositoryId, cancellationToken) is null)
            {
                return BranchCheckResult.Unavailable(MirrorFailure.UnknownProject);
            }

            var fetched = await FetchLockedAsync(request.RepositoryId, cancellationToken);
            if (fetched.Failure != MirrorFailure.None)
            {
                return BranchCheckResult.Unavailable(fetched.Failure, fetched.Detail);
            }

            var remote = await ReadBranchTipAsync(request.RepositoryId, request.Branch, cancellationToken);
            if (!remote.Succeeded)
            {
                return BranchCheckResult.Unavailable(MirrorFailure.GitFailed, remote.Detail);
            }

            return IsWhereWeLeftIt(remote.Tip, request.LastPushedTip, request.StartCommit)
                ? BranchCheckResult.InSync()
                : BranchCheckResult.Moved(await DescribeNotPushedAsync(request.RepositoryId, remote.Tip, request.LastPushedTip, request.StartCommit, cancellationToken));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PushResult> PushAsync(PushRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (_masker is null)
        {
            // Nothing is pushed that has not been scanned, and a scan needs the masker.
            throw new InvalidOperationException("A gateway built without the masker pushes nothing.");
        }

        var gate = _gates.GetOrAdd(request.RepositoryId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        var quarantine = QuarantinePrefix + request.StepRunId.ToString("D");
        try
        {
            return await PushLockedAsync(request, quarantine, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PushResult.Failed(MirrorFailure.GitFailed, Mask(exception.Message, null));
        }
        finally
        {
            // The quarantine name is gone whatever came of the push: nothing of the import stays reachable by name.
            if (_git.MirrorExists(request.RepositoryId))
            {
                await _git.InMirrorAsync(request.RepositoryId, ["update-ref", "-d", quarantine], null, _options.CommandTimeout, CancellationToken.None);
            }

            gate.Release();
        }
    }

    public void DeleteBundle(BundleReference bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var file = BundleFile(bundle);
        if (!file.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_options.BundlesDirectory)) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The file is of no use to anyone; what is left is the cleanup of a crash (Story 3.17), and it names nothing secret.
        }
    }

    private async Task<PushResult> PushLockedAsync(PushRequest request, string quarantine, CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(request.RepositoryId, cancellationToken);
        if (project is null)
        {
            return PushResult.Failed(MirrorFailure.UnknownProject);
        }

        var fetched = await FetchLockedAsync(request.RepositoryId, cancellationToken);
        if (fetched.Failure != MirrorFailure.None)
        {
            return PushResult.Failed(fetched.Failure, fetched.Detail);
        }

        var remote = await ReadBranchTipAsync(request.RepositoryId, request.Branch, cancellationToken);
        if (!remote.Succeeded)
        {
            return PushResult.Failed(MirrorFailure.GitFailed, remote.Detail);
        }

        // An earlier attempt that was made and not recorded: the branch is at the tip, or beyond it, and a push that is a fast-forward is resolved by finding that it was made (AD-5).
        if (remote.Tip is not null && await ContainsAsync(request.RepositoryId, remote.Tip, request.Tip, cancellationToken))
        {
            return PushResult.AlreadyPushed();
        }

        if (await CheckFileAsync(request.Bundle, cancellationToken) is { } file)
        {
            return PushResult.Blocked(PushCheck.Import, file);
        }

        if (!IsWhereWeLeftIt(remote.Tip, request.LastPushedTip, request.StartCommit))
        {
            return PushResult.Blocked(PushCheck.RemoteMoved, await DescribeNotPushedAsync(request.RepositoryId, remote.Tip, request.LastPushedTip, request.StartCommit, cancellationToken));
        }

        if (!await HasCommitAsync(request.RepositoryId, request.StartCommit, cancellationToken) || !await HasCommitAsync(request.RepositoryId, request.LastPushedTip, cancellationToken))
        {
            return PushResult.Failed(MirrorFailure.CommitMissing);
        }

        if (await CheckHeadsAsync(request, cancellationToken) is { } heads)
        {
            return heads;
        }

        if (await ImportAsync(request, quarantine, cancellationToken) is { } imported)
        {
            return imported;
        }

        var ancestor = await _git.InMirrorAsync(request.RepositoryId, ["merge-base", "--is-ancestor", request.LastPushedTip, quarantine], null, _options.CommandTimeout, cancellationToken);
        if (ancestor.ExitCode is > 1 or < 0 || ancestor.TimedOut)
        {
            return PushResult.Failed(MirrorFailure.GitFailed, Describe(ancestor, null));
        }

        if (!ancestor.Succeeded || request.Tip == request.LastPushedTip)
        {
            return PushResult.Blocked(PushCheck.FastForward);
        }

        if (await ScanForSecretsAsync(request, cancellationToken) is { } found)
        {
            return found;
        }

        if (await CheckProtectedPathsAsync(request, cancellationToken) is { } protectedPaths)
        {
            return protectedPaths;
        }

        return await PushToGitHubAsync(request, project, quarantine, cancellationToken);
    }

    /// <summary>
    /// Whether the branch is where Hephaestus left it: at the last pushed tip, or absent when nothing was pushed, which is when the last pushed tip is the commit the Task began from. A branch that is absent after a push was deleted,
    /// and one at any other commit was pushed to by someone else.
    /// </summary>
    private static bool IsWhereWeLeftIt(string? remoteTip, string lastPushedTip, string startCommit) =>
        remoteTip is null ? lastPushedTip == startCommit : remoteTip == lastPushedTip;

    private async Task<(bool Succeeded, string? Tip, string? Detail)> ReadBranchTipAsync(long repositoryId, string branch, CancellationToken cancellationToken)
    {
        var read = await _git.InMirrorAsync(repositoryId, ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}^{{commit}}"], null, _options.CommandTimeout, cancellationToken);
        if (read.Succeeded)
        {
            var tip = read.StandardOutput.Trim();
            return BundleReference.IsCommitId(tip) ? (true, tip, null) : (false, null, "The branch's tip is not a commit id.");
        }

        // With --quiet, a branch that is not there is exit code 1 and nothing printed; any other ending is git failing.
        return read is { ExitCode: 1, TimedOut: false } && string.IsNullOrWhiteSpace(read.StandardError) ? (true, null, null) : (false, null, Describe(read, null));
    }

    /// <summary>What was pushed to the branch that Hephaestus did not push: the commits the branch has and the last pushed tip does not, as abbreviated ids, at most ten; null when the branch is gone.</summary>
    private async Task<string?> DescribeNotPushedAsync(long repositoryId, string? remoteTip, string lastPushedTip, string startCommit, CancellationToken cancellationToken)
    {
        if (remoteTip is null)
        {
            return null;
        }

        // A history that was rewritten no longer has the last pushed tip, and then the commits are those since the Task began.
        var exclude = await HasCommitAsync(repositoryId, lastPushedTip, cancellationToken) ? lastPushedTip : startCommit;
        var listed = await _git.InMirrorAsync(repositoryId, ["rev-list", $"--max-count={CommitsShown + 1}", remoteTip, "--not", exclude], null, _options.CommandTimeout, cancellationToken);
        var commits = listed.Succeeded ? listed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(BundleReference.IsCommitId).ToList() : [];
        if (commits.Count == 0)
        {
            // The branch was moved back to a commit the last pushed tip contains: that commit is what Hephaestus did not push.
            return Abbreviate(remoteTip);
        }

        var shown = string.Join(", ", commits.Take(CommitsShown).Select(Abbreviate));
        return commits.Count > CommitsShown ? shown + ", …" : shown;
    }

    private static string Abbreviate(string commit) => commit[..AbbreviatedLength];

    private async Task<bool> ContainsAsync(long repositoryId, string descendant, string ancestor, CancellationToken cancellationToken)
    {
        if (descendant == ancestor)
        {
            return true;
        }

        if (!await HasCommitAsync(repositoryId, ancestor, cancellationToken))
        {
            return false;
        }

        var check = await _git.InMirrorAsync(repositoryId, ["merge-base", "--is-ancestor", ancestor, descendant], null, _options.CommandTimeout, cancellationToken);
        return check.Succeeded;
    }

    /// <summary>The file of the bundle is the file that was written: a regular file under the bundles directory, of bounded size, with the checksum the reference carries. Null when it is; the reason when not.</summary>
    private async Task<string?> CheckFileAsync(BundleReference bundle, CancellationToken cancellationToken)
    {
        var path = BundleFile(bundle);
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_options.BundlesDirectory)) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return "bundle-file-outside";
        }

        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null)
        {
            return "bundle-file-missing";
        }

        if (info.Length > _options.MaximumBundleBytes)
        {
            return "bundle-file-too-large";
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        return checksum == bundle.Sha256 ? null : "bundle-file-changed";
    }

    private string BundleFile(BundleReference bundle) =>
        Path.GetFullPath(Path.Combine(Path.GetFullPath(_options.BundlesDirectory), bundle.Path.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The bundle carries one head, the Task's branch, at the tip it was said to carry. Null when it does.</summary>
    private async Task<PushResult?> CheckHeadsAsync(PushRequest request, CancellationToken cancellationToken)
    {
        var listed = await _git.InMirrorAsync(request.RepositoryId, ["bundle", "list-heads", BundleFile(request.Bundle)], null, _options.CommandTimeout, cancellationToken);
        if (!listed.Succeeded)
        {
            return PushResult.Blocked(PushCheck.Import, "bundle-unreadable");
        }

        var heads = listed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (heads.Length != 1)
        {
            return PushResult.Blocked(PushCheck.Branch, "bundle-heads");
        }

        var parts = heads[0].Split(' ', 2);
        if (parts.Length != 2 || parts[1] != $"refs/heads/{request.Branch}")
        {
            return PushResult.Blocked(PushCheck.Branch, "bundle-other-ref");
        }

        return parts[0] == request.Tip ? null : PushResult.Blocked(PushCheck.Import, "bundle-tip");
    }

    /// <summary>Imports the bundle into the quarantine ref with strict object checking, and nothing else: no other ref is named. Null when it was imported and holds the commit it was said to.</summary>
    private async Task<PushResult?> ImportAsync(PushRequest request, string quarantine, CancellationToken cancellationToken)
    {
        // A name that a crash left, for this step run's earlier checkpoint, would make a fetch of another tip a non-fast-forward of it, and the work would be blamed.
        await _git.InMirrorAsync(request.RepositoryId, ["update-ref", "-d", quarantine], null, _options.CommandTimeout, cancellationToken);
        var imported = await _git.InMirrorAsync(
            request.RepositoryId,
            ["-c", "transfer.fsckObjects=true", "-c", "fetch.fsckObjects=true", "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", BundleFile(request.Bundle), $"refs/heads/{request.Branch}:{quarantine}"],
            LocalFile,
            _options.FetchTimeout,
            cancellationToken);
        if (!imported.Succeeded)
        {
            return imported.TimedOut ? PushResult.Failed(MirrorFailure.Unreachable) : PushResult.Blocked(PushCheck.Import, "object-check-failed: " + FirstLine(Describe(imported, null)).Replace(BundleFile(request.Bundle), "the bundle", StringComparison.Ordinal).Replace(Mirror(request.RepositoryId), "the mirror", StringComparison.Ordinal));
        }

        var tip = await _git.InMirrorAsync(request.RepositoryId, ["rev-parse", "--verify", "--quiet", $"{quarantine}^{{commit}}"], null, _options.CommandTimeout, cancellationToken);
        return tip.Succeeded && tip.StandardOutput.Trim() == request.Tip ? null : PushResult.Blocked(PushCheck.Import, "bundle-tip");
    }

    private string Mirror(long repositoryId) => _git.MirrorOf(repositoryId);

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    /// <summary>Scans every new commit, tree, and blob with the masker: a commit carries the message, a tree the names, a blob the content (AR-52). Null when none holds a stored secret value.</summary>
    private async Task<PushResult?> ScanForSecretsAsync(PushRequest request, CancellationToken cancellationToken)
    {
        var objects = new List<(string Id, string? Name)>();
        var tooMany = false;
        var listed = await _git.StreamInMirrorAsync(
            request.RepositoryId,
            ["rev-list", "--objects", request.Tip, "--not", request.LastPushedTip],
            null,
            _options.FetchTimeout,
            async (stream, token) =>
            {
                await foreach (var line in ReadLinesAsync(stream, token))
                {
                    var split = line.IndexOf(' ', StringComparison.Ordinal);
                    objects.Add(split < 0 || split == line.Length - 1 ? (line[..(split < 0 ? line.Length : split)], null) : (line[..split], line[(split + 1)..]));
                    if (objects.Count > MaximumObjects)
                    {
                        tooMany = true;
                        return false;
                    }
                }

                return true;
            },
            cancellationToken);
        if (tooMany)
        {
            return PushResult.Blocked(PushCheck.Import, "too-many-objects");
        }

        if (!listed.Succeeded)
        {
            return PushResult.Failed(listed.TimedOut ? MirrorFailure.Unreachable : MirrorFailure.GitFailed, Describe(listed, null));
        }

        var state = new ScanState();
        var read = await _git.StreamInMirrorAsync(
            request.RepositoryId,
            ["cat-file", "--batch"],
            objects.Select(o => o.Id),
            _options.FetchTimeout,
            (stream, token) => ScanObjectsAsync(stream, objects, state, token),
            cancellationToken);
        if (state.Findings.Count > 0)
        {
            return PushResult.Blocked(PushCheck.Secret, Bound(string.Join(", ", state.Findings)));
        }

        // A scan that did not read every object is not a scan: nothing is pushed on what was not looked at.
        if (!read.Succeeded || state.Scanned != objects.Count)
        {
            return PushResult.Failed(read.TimedOut ? MirrorFailure.Unreachable : MirrorFailure.GitFailed, read.Succeeded ? "The scan did not read every object." : Describe(read, null));
        }

        return null;
    }

    private sealed class ScanState
    {
        public int Scanned { get; set; }

        public List<string> Findings { get; } = [];
    }

    /// <summary>The lines of git's output, ended by a line feed and by nothing else: a carriage return is a character that a file name may hold, and it must not end a line.</summary>
    private static async IAsyncEnumerable<string> ReadLinesAsync(Stream stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var line = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (character == '\n')
                {
                    yield return line.ToString();
                    line.Clear();
                }
                else
                {
                    line.Append(character);
                }
            }
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    /// <summary>Reads the output of <c>git cat-file --batch</c>, <c>&lt;id&gt; &lt;type&gt; &lt;size&gt;</c>, the content, and a line feed for each object, in the order the ids were given, and scans the content as it comes.</summary>
    private async Task<bool> ScanObjectsAsync(Stream stream, List<(string Id, string? Name)> objects, ScanState state, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var start = 0;
        var end = 0;
        var chars = new char[buffer.Length + 4];

        async ValueTask<bool> FillAsync()
        {
            if (start > 0)
            {
                Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                end -= start;
                start = 0;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(end), cancellationToken);
            end += read;
            return read > 0;
        }

        foreach (var (id, name) in objects)
        {
            int newline;
            while ((newline = Array.IndexOf(buffer, (byte)'\n', start, end - start)) < 0)
            {
                if (!await FillAsync())
                {
                    return true;
                }
            }

            var header = Encoding.ASCII.GetString(buffer, start, newline - start).Split(' ');
            var type = header.Length == 3 ? header[1] : "object";
            start = newline + 1;
            if (header.Length != 3 || !long.TryParse(header[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size))
            {
                // A missing object is what the import should have made impossible; it has no content to scan, and the push is not made on an object nobody could read.
                return false;
            }

            var scan = _masker!.CreateScan();
            var decoder = Encoding.UTF8.GetDecoder();
            for (var remaining = size; remaining > 0;)
            {
                if (start == end && !await FillAsync())
                {
                    return true;
                }

                var take = (int)Math.Min(remaining, end - start);
                var count = decoder.GetChars(buffer, start, take, chars, 0, flush: take == remaining);
                scan.Write(chars.AsSpan(0, count));
                start += take;
                remaining -= take;
            }

            // The line feed that ends the content.
            while (start == end)
            {
                if (!await FillAsync())
                {
                    return true;
                }
            }

            start++;
            state.Scanned++;
            if (scan.Found)
            {
                state.Findings.Add(Mask(name ?? $"{type} {Abbreviate(id)}", null));
                if (state.Findings.Count >= MaximumFindings)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Checks the diff of the Task's start commit and the tip against the protected paths (AD-19). Null when it touches none.</summary>
    private async Task<PushResult?> CheckProtectedPathsAsync(PushRequest request, CancellationToken cancellationToken)
    {
        var globs = await _reads.ListProtectedPathsAsync(cancellationToken);
        if (globs.Count == 0)
        {
            return null;
        }

        var touched = new List<string>();
        var diff = await _git.StreamInMirrorAsync(
            request.RepositoryId,
            ["diff-tree", "-r", "-z", "--name-only", "--no-renames", request.StartCommit, request.Tip],
            null,
            _options.CommandTimeout,
            async (stream, token) =>
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var path = new StringBuilder();
                var buffer = new char[4096];
                int read;
                while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
                {
                    foreach (var character in buffer.AsSpan(0, read))
                    {
                        if (character != '\0')
                        {
                            path.Append(character);
                            continue;
                        }

                        if (path.Length > 0 && ProtectedPathMatcher.IsProtected(path.ToString(), globs))
                        {
                            touched.Add(path.ToString());
                        }

                        path.Clear();
                        if (touched.Count >= MaximumFindings)
                        {
                            return false;
                        }
                    }
                }

                return true;
            },
            cancellationToken);
        if (touched.Count > 0)
        {
            return PushResult.Blocked(PushCheck.ProtectedPath, Bound(Mask(string.Join(", ", touched), null)));
        }

        return diff.Succeeded ? null : PushResult.Failed(diff.TimedOut ? MirrorFailure.Unreachable : MirrorFailure.GitFailed, Describe(diff, null));
    }

    /// <summary>The push itself: the quarantine ref to the Task branch, as a fast-forward, with the token in the environment of this one process.</summary>
    private async Task<PushResult> PushToGitHubAsync(PushRequest request, Project project, string quarantine, CancellationToken cancellationToken)
    {
        var token = await _tokens(request.RepositoryId, cancellationToken);
        if (token.Failure != MirrorFailure.None)
        {
            return PushResult.Failed(token.Failure);
        }

        var url = _options.RemoteUrl(project.FullName);
        if (TokenEnvironment(url, token.Token!) is not { } environment)
        {
            return PushResult.Failed(MirrorFailure.GitFailed, "The address of the repository is not an absolute address.");
        }

        // One refspec, with no plus: git itself refuses what is not a fast-forward.
        var pushed = await _git.InMirrorAsync(request.RepositoryId, ["push", "--porcelain", "--no-follow-tags", url, $"{quarantine}:refs/heads/{request.Branch}"], environment, _options.FetchTimeout, cancellationToken);
        if (pushed.Succeeded)
        {
            // The mirror follows the branch, so that the next bundle and the next check do not wait for a fetch.
            await _git.InMirrorAsync(request.RepositoryId, ["update-ref", $"refs/heads/{request.Branch}", request.Tip], null, _options.CommandTimeout, cancellationToken);
            return PushResult.Pushed();
        }

        if (!pushed.TimedOut && (pushed.StandardOutput.Contains("[rejected]", StringComparison.Ordinal) || pushed.StandardError.Contains("[rejected]", StringComparison.Ordinal)))
        {
            // Someone pushed to the branch between the fetch and the push, and GitHub refused what was no fast-forward of it.
            var fetched = await FetchLockedAsync(request.RepositoryId, cancellationToken);
            if (fetched.Failure != MirrorFailure.None)
            {
                return PushResult.Failed(fetched.Failure, fetched.Detail);
            }

            var remote = await ReadBranchTipAsync(request.RepositoryId, request.Branch, cancellationToken);
            return remote.Succeeded
                ? PushResult.Blocked(PushCheck.RemoteMoved, await DescribeNotPushedAsync(request.RepositoryId, remote.Tip, request.LastPushedTip, request.StartCommit, cancellationToken))
                : PushResult.Failed(MirrorFailure.GitFailed, remote.Detail);
        }

        return PushResult.Failed(Classify(pushed), Describe(pushed, token.Token));
    }

    private static string Bound(string text) => text.Length > DetailLimit ? text[..DetailLimit] : text;
}
