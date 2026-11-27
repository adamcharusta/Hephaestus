using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Secrets;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.GitHub;

/// <summary>
/// The bare mirrors of the Projects' repositories and the bundles made from them (AD-4, AD-9, Story 3.5). Git runs in a mirror and nowhere else, through <see cref="GitProcess"/>, so
/// that it cannot run in a directory an Agent can write to, and a mirror has no hooks and no working tree: it is made with an empty template and every command switches hooks off. The
/// installation token is put in the environment of the one git process that fetches, as a header for GitHub's own address, and is never an argument, a file, or a remote of the
/// mirror, so that nothing on the data volume holds it. A mirror is a cache: it can be removed and fetched again at any time.
/// </summary>
public sealed class GitHubMirrors : IRepositoryMirrors
{
    private const int DetailLimit = 500;
    private static readonly string[] Unreachable = ["Could not resolve host", "Failed to connect", "timed out", "early EOF", "RPC failed", "returned error: 5", "Connection reset", "unable to access", "Could not read from remote"];

    private readonly IReadStore _reads;
    private readonly GitMirrorOptions _options;
    private readonly GitProcess _git;
    private readonly SecretMasker? _masker;
    private readonly Func<long, CancellationToken, Task<MirrorToken>> _tokens;
    private readonly Action? _forgetTokens;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _gates = new();

    public GitHubMirrors(
        IReadStore reads,
        ISecretStore secrets,
        TimeProvider time,
        GitHubRateLimit rateLimit,
        GitMirrorOptions options,
        Func<HttpMessageHandler>? handler = null,
        SecretMasker? masker = null)
        : this(reads, options, masker, CreateTokens(reads, secrets, time, rateLimit, handler, out var forget), forget)
    {
    }

    internal GitHubMirrors(IReadStore reads, GitMirrorOptions options, SecretMasker? masker, Func<long, CancellationToken, Task<MirrorToken>> tokens, Action? forgetTokens = null)
    {
        _reads = reads;
        _options = options;
        _git = new GitProcess(options);
        _masker = masker;
        _tokens = tokens;
        _forgetTokens = forgetTokens;
    }

    public async Task<MirrorResult> FetchAsync(long repositoryId, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(repositoryId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await FetchLockedAsync(repositoryId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<BundleResult> CreateBundleAsync(BundleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var gate = _gates.GetOrAdd(request.RepositoryId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // A removed Project gets no bundle, whatever its mirror still holds.
            if (await FindProjectAsync(request.RepositoryId, cancellationToken) is null)
            {
                return BundleResult.Failed(MirrorFailure.UnknownProject);
            }

            if (!await HasCommitAsync(request.RepositoryId, request.Commit, cancellationToken))
            {
                var fetched = await FetchLockedAsync(request.RepositoryId, cancellationToken);
                if (fetched.Failure != MirrorFailure.None)
                {
                    return BundleResult.Failed(fetched.Failure, fetched.Detail);
                }

                if (!await HasCommitAsync(request.RepositoryId, request.Commit, cancellationToken))
                {
                    return BundleResult.Failed(MirrorFailure.CommitMissing);
                }
            }

            return await WriteBundleAsync(request, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MirrorResult> FetchLockedAsync(long repositoryId, CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(repositoryId, cancellationToken);
        if (project is null)
        {
            return MirrorResult.Failed(MirrorFailure.UnknownProject);
        }

        var token = await _tokens(repositoryId, cancellationToken);
        if (token.Failure != MirrorFailure.None)
        {
            return MirrorResult.Failed(token.Failure);
        }

        // A mirror that is not whole, such as one a crash left half made, is removed and made again: it is a cache.
        if (_git.MirrorExists(repositoryId) && !await IsWholeAsync(repositoryId, cancellationToken))
        {
            try
            {
                _git.RemoveMirror(repositoryId);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return MirrorResult.Failed(MirrorFailure.GitFailed, Mask(exception.Message, token.Token));
            }
        }

        if (!_git.MirrorExists(repositoryId))
        {
            var made = await _git.InitAsync(repositoryId, cancellationToken);
            if (!made.Succeeded)
            {
                return MirrorResult.Failed(MirrorFailure.GitFailed, Describe(made, token.Token));
            }
        }

        // The header is scoped to GitHub's address, so a redirect to another host does not carry it; it is in the environment of this one process and nowhere else.
        var url = _options.RemoteUrl(project.FullName);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address))
        {
            return MirrorResult.Failed(MirrorFailure.GitFailed, "The address of the repository is not an absolute address.");
        }

        var scope = address.GetLeftPart(UriPartial.Authority);
        var environment = new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = $"http.{scope}/.extraHeader",
            ["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + token.Token)),
        };
        var fetched = await _git.InMirrorAsync(
            repositoryId,
            ["fetch", "--quiet", "--prune", "--no-tags", "--no-write-fetch-head", "--force", url, "+refs/heads/*:refs/heads/*"],
            environment,
            _options.FetchTimeout,
            cancellationToken);
        return fetched.Succeeded ? MirrorResult.Fetched : MirrorResult.Failed(Classify(fetched), Describe(fetched, token.Token));
    }

    private async Task<Project?> FindProjectAsync(long repositoryId, CancellationToken cancellationToken) =>
        (await _reads.ListProjectsAsync(includeRemoved: false, cancellationToken)).FirstOrDefault(p => p.RepositoryId == repositoryId);

    private async Task<bool> IsWholeAsync(long repositoryId, CancellationToken cancellationToken)
    {
        var check = await _git.InMirrorAsync(repositoryId, ["rev-parse", "--is-bare-repository"], null, _options.CommandTimeout, cancellationToken);
        return check.Succeeded && check.StandardOutput.Trim() == "true";
    }

    private async Task<bool> HasCommitAsync(long repositoryId, string commit, CancellationToken cancellationToken)
    {
        if (!_git.MirrorExists(repositoryId))
        {
            return false;
        }

        var found = await _git.InMirrorAsync(repositoryId, ["cat-file", "-e", $"{commit}^{{commit}}"], null, _options.CommandTimeout, cancellationToken);
        return found.Succeeded;
    }

    private async Task<BundleResult> WriteBundleAsync(BundleRequest request, CancellationToken cancellationToken)
    {
        var relative = $"{request.TaskId:D}/{request.StepRunId:D}-checkout{BundleReference.Extension}";
        var final = Path.Combine(Path.GetFullPath(_options.BundlesDirectory), relative.Replace('/', Path.DirectorySeparatorChar));
        var temporary = Path.Combine(Path.GetDirectoryName(final)!, $".{Path.GetFileName(final)}.tmp");
        var reference = $"refs/hephaestus/bundle/{request.StepRunId:D}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(final)!);
            // A bundle needs a name for what it carries; the commit gets one in the mirror for as long as the bundle takes, and the name is gone when it is made.
            // The bound is enforced before anything is written where git can say how much there is: the disk usage of every object the commit needs, which is about what the bundle holds.
            var usage = await _git.InMirrorAsync(request.RepositoryId, ["rev-list", "--objects", "--disk-usage", request.Commit], null, _options.CommandTimeout, cancellationToken);
            if (!usage.Succeeded || !long.TryParse(usage.StandardOutput.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var bytes))
            {
                return BundleResult.Failed(MirrorFailure.GitFailed, Describe(usage, null));
            }

            if (bytes > _options.MaximumBundleBytes)
            {
                return BundleResult.Failed(MirrorFailure.TooLarge);
            }

            var named = await _git.InMirrorAsync(request.RepositoryId, ["update-ref", reference, request.Commit], null, _options.CommandTimeout, cancellationToken);
            if (!named.Succeeded)
            {
                return BundleResult.Failed(MirrorFailure.GitFailed, Describe(named, null));
            }

            try
            {
                var made = await _git.InMirrorAsync(request.RepositoryId, ["bundle", "create", "--quiet", temporary, reference], null, _options.CommandTimeout, cancellationToken);
                if (!made.Succeeded)
                {
                    return BundleResult.Failed(MirrorFailure.GitFailed, Describe(made, null));
                }
            }
            finally
            {
                await _git.InMirrorAsync(request.RepositoryId, ["update-ref", "-d", reference], null, _options.CommandTimeout, CancellationToken.None);
            }

            if (new FileInfo(temporary).Length > _options.MaximumBundleBytes)
            {
                return BundleResult.Failed(MirrorFailure.TooLarge);
            }

            var heads = await _git.InMirrorAsync(request.RepositoryId, ["bundle", "list-heads", temporary], null, _options.CommandTimeout, cancellationToken);
            var lines = heads.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!heads.Succeeded || lines.Length != 1 || !lines[0].StartsWith(request.Commit + " ", StringComparison.Ordinal))
            {
                return BundleResult.Failed(MirrorFailure.GitFailed, "The bundle does not hold exactly the commit it was made for.");
            }

            string checksum;
            await using (var file = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
            }

            File.Move(temporary, final, overwrite: true);
            return BundleResult.Made(new BundleReference(relative, checksum, request.Commit));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BundleResult.Failed(MirrorFailure.GitFailed, Mask(exception.Message, null));
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private MirrorFailure Classify(GitOutput output)
    {
        if (output.TimedOut)
        {
            return MirrorFailure.Unreachable;
        }

        if (output.ExitCode < 0)
        {
            return MirrorFailure.GitFailed;
        }

        var text = output.StandardError;

        // GitHub's rate limits answer 403 and 429 like a refusal: they are a wait, and the token is a good one.
        if (text.Contains("returned error: 429", StringComparison.Ordinal) || text.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
        {
            return MirrorFailure.Unreachable;
        }

        if (text.Contains("returned error: 401", StringComparison.Ordinal) || text.Contains("returned error: 403", StringComparison.Ordinal) || text.Contains("Authentication failed", StringComparison.Ordinal))
        {
            // The token was not accepted; the next request asks GitHub for another.
            _forgetTokens?.Invoke();
            return MirrorFailure.Rejected;
        }

        if (text.Contains("returned error: 404", StringComparison.Ordinal) || text.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return MirrorFailure.NotInstalled;
        }

        return Unreachable.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ? MirrorFailure.Unreachable : MirrorFailure.Rejected;
    }

    private string Describe(GitOutput output, string? token) => Mask(string.IsNullOrWhiteSpace(output.StandardError) ? $"git ended with exit code {output.ExitCode}" : output.StandardError.Trim(), token);

    /// <summary>What git said, with the token and every stored secret masked, and short: it goes to a log and to nothing that GitHub reads.</summary>
    private string Mask(string text, string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            text = text.Replace(token, SecretMasker.Replacement, StringComparison.Ordinal)
                .Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + token)), SecretMasker.Replacement, StringComparison.Ordinal);
        }

        text = _masker?.Mask(text) ?? text;
        return text.Length > DetailLimit ? text[..DetailLimit] : text;
    }

    private static Func<long, CancellationToken, Task<MirrorToken>> CreateTokens(IReadStore reads, ISecretStore secrets, TimeProvider time, GitHubRateLimit limit, Func<HttpMessageHandler>? handler, out Action forget)
    {
        var tokens = new MirrorTokens(reads, secrets, time, limit, handler ?? (() => Octokit.Internal.HttpMessageHandlerFactory.CreateDefault()));
        forget = tokens.Forget;
        return tokens.TokenAsync;
    }
}
