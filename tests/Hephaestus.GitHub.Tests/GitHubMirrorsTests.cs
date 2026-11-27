using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Secrets;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.GitHub.Tests;

/// <summary>
/// Story 3.5, AD-4, AD-9, FR-17, FR-18: the gateway keeps one bare mirror per Project and makes a bundle of one commit from it. A repository on this machine stands in for GitHub, with real git. The
/// mirror has no hooks and no working tree; the token is in the environment of the one git process that fetches, and nowhere on the data volume; git runs in a mirror and nowhere else.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class GitHubMirrorsTests : IDisposable
{
    private const long RepositoryId = 42;
    private const string Token = "ghs_mirror-token-0123456789";
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");

    private readonly string _root = Directory.CreateTempSubdirectory("hephaestus-mirrors-").FullName;
    private readonly FakeStores _stores = new();
    private readonly string _mirrors;
    private readonly string _bundles;
    private (string Path, string Main, string First, string TaskBranchTip) _remote;

    public GitHubMirrorsTests()
    {
        _mirrors = Path.Combine(_root, "mirrors");
        _bundles = Path.Combine(_root, "bundles");
        _stores.Projects.Add(new Project(RepositoryId, "acme/web", "main", new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero), null));
        if (TestGit.Available)
        {
            _remote = TestGit.Repository(Path.Combine(_root, "remote"));
        }
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private GitMirrorOptions Options(string? git = null, Func<string, string>? remote = null) =>
        new(_mirrors, _bundles) { AllowedProtocols = "file", RemoteUrl = remote ?? (_ => new Uri(_remote.Path).AbsoluteUri), GitExecutable = git ?? "git", FetchTimeout = TimeSpan.FromSeconds(60), CommandTimeout = TimeSpan.FromSeconds(60) };

    private GitHubMirrors Mirrors(GitMirrorOptions? options = null, MirrorToken? token = null, SecretMasker? masker = null) =>
        new(_stores, options ?? Options(), masker, (_, _) => Task.FromResult(token ?? MirrorToken.Of(Token)));

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string Mirror => Path.Combine(_mirrors, $"{RepositoryId}.git");

    /// <summary>A stand-in for <c>git</c> that writes what it was started with, and then runs git: the arguments and the environment of the one process.</summary>
    private string Recording(string extra = "", bool onlyFetch = false)
    {
        if (onlyFetch)
        {
            extra = $"case \"$*\" in *fetch*) {extra};; esac";
        }

        var log = Path.Combine(_root, "git.log");
        var script = Path.Combine(_root, "git-recording");
        File.WriteAllText(script, $"#!/bin/sh\n{{ echo \"ARGS: $*\"; env | sort; echo END; }} >> '{log}'\n{extra}\nexec git \"$@\"\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    private string Log => File.ReadAllText(Path.Combine(_root, "git.log"));

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_first_fetch_makes_a_bare_mirror_with_no_hooks_no_working_tree_and_no_remote()
    {
        var result = await Mirrors().FetchAsync(RepositoryId, Cancel);

        Assert.Equal(MirrorFailure.None, result.Failure);
        Assert.Equal("true", TestGit.Run(_root, $"--git-dir={Mirror}", "rev-parse", "--is-bare-repository"));
        Assert.False(Directory.Exists(Path.Combine(Mirror, "hooks")));
        Assert.False(File.Exists(Path.Combine(Mirror, "info", "exclude")));
        Assert.False(Directory.Exists(Path.Combine(_mirrors, $"{RepositoryId}")));
        var configuration = TestGit.Run(_root, $"--git-dir={Mirror}", "config", "--local", "--list");
        Assert.DoesNotContain("remote.", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("url", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("extraheader", configuration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_mirror_holds_every_branch_of_the_repository_and_a_later_fetch_brings_the_new_commits_and_drops_the_branches_that_are_gone()
    {
        var mirrors = Mirrors();
        await mirrors.FetchAsync(RepositoryId, Cancel);
        Assert.Equal(_remote.Main, TestGit.Run(_root, $"--git-dir={Mirror}", "rev-parse", "refs/heads/main"));
        Assert.Equal(_remote.First, TestGit.Run(_root, $"--git-dir={Mirror}", "rev-parse", "refs/heads/hephaestus/1-task"));

        File.WriteAllText(Path.Combine(_remote.Path, "third.txt"), "third\n");
        TestGit.Run(_remote.Path, "add", "-A");
        TestGit.Run(_remote.Path, "commit", "--quiet", "-m", "third");
        TestGit.Run(_remote.Path, "branch", "-D", "hephaestus/1-task");
        var result = await mirrors.FetchAsync(RepositoryId, Cancel);

        Assert.Equal(MirrorFailure.None, result.Failure);
        Assert.Equal(TestGit.Run(_remote.Path, "rev-parse", "HEAD"), TestGit.Run(_root, $"--git-dir={Mirror}", "rev-parse", "refs/heads/main"));
        Assert.Equal("refs/heads/main", TestGit.Run(_root, $"--git-dir={Mirror}", "for-each-ref", "--format=%(refname)", "refs/heads"));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_token_is_in_the_environment_of_the_fetching_process_as_a_header_for_the_address_and_in_no_argument()
    {
        await Mirrors(Options(git: Recording())).FetchAsync(RepositoryId, Cancel);

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + Token));
        Assert.Contains($"GIT_CONFIG_VALUE_0=Authorization: Basic {basic}", Log, StringComparison.Ordinal);
        Assert.Contains("GIT_CONFIG_KEY_0=http.file://", Log, StringComparison.Ordinal);
        Assert.Contains("GIT_CONFIG_COUNT=1", Log, StringComparison.Ordinal);
        foreach (var line in Log.Split('\n').Where(l => l.StartsWith("ARGS:", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(Token, line, StringComparison.Ordinal);
            Assert.DoesNotContain(basic, line, StringComparison.Ordinal);
            Assert.DoesNotContain("extraHeader", line, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task Nothing_on_the_mirrors_or_the_bundles_volume_holds_the_token_after_a_fetch_and_a_bundle()
    {
        var mirrors = Mirrors();
        await mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, StepRunId), Cancel);

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + Token));
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Where(f => !f.StartsWith(_remote.Path + "/", StringComparison.Ordinal)))
        {
            var bytes = await File.ReadAllBytesAsync(file, Cancel);
            Assert.False(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Token)) >= 0, file);
            Assert.False(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(basic)) >= 0, file);
        }
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task Every_git_process_has_no_hook_no_system_or_user_configuration_no_prompt_a_clean_environment_and_only_the_protocols_it_may_use()
    {
        Environment.SetEnvironmentVariable("HEPHAESTUS_TEST_AMBIENT", "must-not-reach-git");
        try
        {
            await Mirrors(Options(git: Recording())).FetchAsync(RepositoryId, Cancel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HEPHAESTUS_TEST_AMBIENT", null);
        }

        Assert.Contains("-c core.hooksPath=/dev/null", Log, StringComparison.Ordinal);
        Assert.Contains("core.fsmonitor=false", Log, StringComparison.Ordinal);
        Assert.Contains("GIT_CONFIG_NOSYSTEM=1", Log, StringComparison.Ordinal);
        Assert.Contains("GIT_CONFIG_GLOBAL=/dev/null", Log, StringComparison.Ordinal);
        Assert.Contains("GIT_TERMINAL_PROMPT=0", Log, StringComparison.Ordinal);
        Assert.Contains("GIT_ALLOW_PROTOCOL=file", Log, StringComparison.Ordinal);
        Assert.Contains($"HOME={_mirrors}", Log, StringComparison.Ordinal);
        Assert.DoesNotContain("HEPHAESTUS_TEST_AMBIENT", Log, StringComparison.Ordinal);
        Assert.All(Log.Split('\n').Where(l => l.StartsWith("ARGS:", StringComparison.Ordinal) && l.Contains("--git-dir", StringComparison.Ordinal)), line => Assert.Contains($"--git-dir={Mirror}", line, StringComparison.Ordinal));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_hook_planted_in_the_mirror_does_not_run()
    {
        var mirrors = Mirrors();
        await mirrors.FetchAsync(RepositoryId, Cancel);
        var marker = Path.Combine(_root, "hook-ran");
        Directory.CreateDirectory(Path.Combine(Mirror, "hooks"));
        foreach (var hook in new[] { "reference-transaction", "post-update", "pre-auto-gc", "post-checkout" })
        {
            var path = Path.Combine(Mirror, "hooks", hook);
            File.WriteAllText(path, $"#!/bin/sh\ntouch '{marker}'\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        File.WriteAllText(Path.Combine(_remote.Path, "third.txt"), "third\n");
        TestGit.Run(_remote.Path, "add", "-A");
        TestGit.Run(_remote.Path, "commit", "--quiet", "-m", "third");
        var fetched = await mirrors.FetchAsync(RepositoryId, Cancel);
        var bundle = await mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, StepRunId), Cancel);

        Assert.Equal(MirrorFailure.None, fetched.Failure);
        Assert.Equal(MirrorFailure.None, bundle.Failure);
        Assert.False(File.Exists(marker));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_of_a_commit_is_one_file_with_one_head_its_checksum_and_the_whole_history()
    {
        var mirrors = Mirrors();
        await mirrors.FetchAsync(RepositoryId, Cancel);

        var result = await mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, StepRunId), Cancel);

        Assert.Equal(MirrorFailure.None, result.Failure);
        var bundle = result.Bundle!;
        Assert.Equal($"{TaskId}/{StepRunId}-checkout.bundle", bundle.Path);
        Assert.Equal(_remote.Main, bundle.Tip);
        var file = Path.Combine(_bundles, bundle.Path);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file, Cancel))), bundle.Sha256);
        Assert.Equal($"{_remote.Main} refs/hephaestus/bundle/{StepRunId}", TestGit.Run(_root, "bundle", "list-heads", file));
        TestGit.Run(_remote.Path, "bundle", "verify", file);
        var clone = Path.Combine(_root, "clone");
        TestGit.Run(_root, "init", "--quiet", clone);
        TestGit.Run(clone, "fetch", "--quiet", file, $"refs/hephaestus/bundle/{StepRunId}:refs/heads/fetched");
        Assert.Equal(_remote.Main, TestGit.Run(clone, "rev-parse", "refs/heads/fetched"));
        Assert.Equal("2", TestGit.Run(clone, "rev-list", "--count", "refs/heads/fetched"));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(file)!, ".*"));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_name_the_commit_is_given_for_the_bundle_is_gone_from_the_mirror_afterwards()
    {
        var mirrors = Mirrors();

        await mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, StepRunId), Cancel);

        Assert.Equal(string.Empty, TestGit.Run(_root, $"--git-dir={Mirror}", "for-each-ref", "--format=%(refname)", "refs/hephaestus"));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_commit_the_mirror_lacks_is_looked_for_by_fetching_it_once()
    {
        var mirrors = Mirrors(Options(git: Recording()));

        var result = await mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.First, TaskId, StepRunId), Cancel);

        Assert.Equal(MirrorFailure.None, result.Failure);
        Assert.Equal(1, Log.Split('\n').Count(l => l.StartsWith("ARGS:", StringComparison.Ordinal) && l.Contains(" fetch ", StringComparison.Ordinal)));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_commit_the_repository_does_not_have_is_missing_even_after_the_fetch()
    {
        var result = await Mirrors().CreateBundleAsync(new BundleRequest(RepositoryId, new string('a', 40), TaskId, StepRunId), Cancel);

        Assert.Equal(MirrorFailure.CommitMissing, result.Failure);
        Assert.Null(result.Bundle);
        Assert.False(Directory.Exists(Path.Combine(_bundles, TaskId.ToString())) && Directory.EnumerateFileSystemEntries(Path.Combine(_bundles, TaskId.ToString())).Any());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_over_the_bound_is_not_made_and_leaves_no_file()
    {
        var options = Options() with { MaximumBundleBytes = 10 };

        var result = await Mirrors(options).CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, StepRunId), Cancel);

        Assert.Equal(MirrorFailure.TooLarge, result.Failure);
        Assert.Empty(Directory.EnumerateFiles(_bundles, "*", SearchOption.AllDirectories));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_second_bundle_for_the_same_step_run_replaces_the_first()
    {
        var mirrors = Mirrors();
        var first = await mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.First, TaskId, StepRunId), Cancel);
        var second = await mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, StepRunId), Cancel);

        Assert.Equal(first.Bundle!.Path, second.Bundle!.Path);
        Assert.NotEqual(first.Bundle.Sha256, second.Bundle.Sha256);
        Assert.Single(Directory.EnumerateFiles(_bundles, "*", SearchOption.AllDirectories));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task Bundles_for_two_step_runs_made_together_are_both_made()
    {
        var mirrors = Mirrors();
        var other = Guid.NewGuid();

        var results = await Task.WhenAll(
            mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, StepRunId), Cancel),
            mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.First, TaskId, other), Cancel));

        Assert.All(results, result => Assert.Equal(MirrorFailure.None, result.Failure));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_project_that_is_not_registered_is_unknown_and_makes_no_mirror()
    {
        var result = await Mirrors().FetchAsync(7, Cancel);

        Assert.Equal(MirrorFailure.UnknownProject, result.Failure);
        Assert.False(Directory.Exists(_mirrors));
    }

    [Theory(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    [InlineData(MirrorFailure.NotConnected)]
    [InlineData(MirrorFailure.KeyUnreadable)]
    [InlineData(MirrorFailure.NotInstalled)]
    [InlineData(MirrorFailure.Unreachable)]
    [InlineData(MirrorFailure.Rejected)]
    public async Task What_the_token_lookup_reports_is_what_the_fetch_and_the_bundle_report(MirrorFailure failure)
    {
        var mirrors = Mirrors(token: MirrorToken.Failed(failure));

        Assert.Equal(failure, (await mirrors.FetchAsync(RepositoryId, Cancel)).Failure);
        Assert.Equal(failure, (await mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, StepRunId), Cancel)).Failure);
        Assert.False(Directory.Exists(Mirror));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_repository_git_cannot_fetch_is_reported_and_what_git_said_is_masked()
    {
        var script = Recording("echo \"fatal: bad credential ghs_mirror-token-0123456789 and sk-stored-secret\" >&2; exit 128", onlyFetch: true);

        var result = await Mirrors(Options(git: script)).FetchAsync(RepositoryId, Cancel);

        Assert.Equal(MirrorFailure.Rejected, result.Failure);
        Assert.DoesNotContain(Token, result.Detail, StringComparison.Ordinal);
        Assert.Contains("***", result.Detail, StringComparison.Ordinal);
    }

    [Theory(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    [InlineData("fatal: unable to access 'https://x/': The requested URL returned error: 403", MirrorFailure.Rejected)]
    [InlineData("fatal: Authentication failed for 'https://x/'", MirrorFailure.Rejected)]
    [InlineData("fatal: unable to access 'https://x/': The requested URL returned error: 404", MirrorFailure.NotInstalled)]
    [InlineData("fatal: repository 'https://x/' not found", MirrorFailure.NotInstalled)]
    [InlineData("fatal: unable to access 'https://x/': Could not resolve host: x", MirrorFailure.Unreachable)]
    [InlineData("error: RPC failed; curl 92 HTTP/2 stream 0 was not closed cleanly", MirrorFailure.Unreachable)]
    [InlineData("fatal: something else entirely", MirrorFailure.Rejected)]
    public async Task What_git_says_decides_whether_the_repository_is_rejected_not_installed_or_unreachable(string said, MirrorFailure expected)
    {
        var script = Recording($"echo \"{said}\" >&2; exit 128", onlyFetch: true);

        var result = await Mirrors(Options(git: script)).FetchAsync(RepositoryId, Cancel);

        Assert.Equal(expected, result.Failure);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_token_the_installation_gives_is_the_one_the_fetch_carries()
    {
        _stores.AppId = 123456;
        _stores.Secrets[GitHubAppCredential.PrivateKeySecretName] = RSA.Create(2048).ExportRSAPrivateKeyPem();
        _stores.Projects.Add(new Project(1, "acme/web", "main", new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero), null));
        var mirrors = new GitHubMirrors(_stores, _stores, TimeProvider.System, new GitHubRateLimit(TimeProvider.System), Options(git: Recording()), () => new FakeGitHub());

        var result = await mirrors.FetchAsync(1, Cancel);

        Assert.Equal(MirrorFailure.None, result.Failure);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + FakeGitHub.InstallationToken));
        Assert.Contains($"GIT_CONFIG_VALUE_0=Authorization: Basic {basic}", Log, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(_mirrors, "1.git")));
    }

    [Fact]
    public async Task A_git_that_cannot_be_started_is_a_failure_of_git()
    {
        var result = await Mirrors(Options(git: Path.Combine(_root, "no-such-git"))).FetchAsync(RepositoryId, Cancel);

        Assert.Equal(MirrorFailure.GitFailed, result.Failure);
        Assert.False(string.IsNullOrEmpty(result.Detail));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_git_that_takes_too_long_is_killed_and_reported_as_unreachable()
    {
        var script = Recording("sleep 30", onlyFetch: true);
        var options = Options(git: script) with { FetchTimeout = TimeSpan.FromMilliseconds(300) };

        var result = await Mirrors(options).FetchAsync(RepositoryId, Cancel);

        Assert.Equal(MirrorFailure.Unreachable, result.Failure);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_stored_secret_in_what_git_said_is_masked_too()
    {
        _stores.Secrets["agent-key"] = "sk-stored-secret-value";
        var script = Recording("echo \"fatal: sk-stored-secret-value\" >&2; exit 128", onlyFetch: true);

        var masker = new SecretMasker(_stores);
        await masker.RebuildAsync(Cancel);

        var result = await Mirrors(Options(git: script), masker: masker).FetchAsync(RepositoryId, Cancel);

        Assert.DoesNotContain("sk-stored-secret-value", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bundle_is_asked_for_with_a_request()
    {
        var mirrors = Mirrors();

        await Assert.ThrowsAsync<ArgumentNullException>(() => mirrors.CreateBundleAsync(null!, Cancel));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_removed_project_gets_no_bundle_even_when_its_mirror_still_holds_the_commit()
    {
        var mirrors = Mirrors();
        await mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, StepRunId), Cancel);
        _stores.Projects.Clear();

        var result = await mirrors.CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, Guid.NewGuid()), Cancel);

        Assert.Equal(MirrorFailure.UnknownProject, result.Failure);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_mirror_a_crash_left_half_made_is_removed_and_made_again()
    {
        Directory.CreateDirectory(Mirror);
        File.WriteAllText(Path.Combine(Mirror, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(Mirror, "junk"), "not a repository");

        var result = await Mirrors().FetchAsync(RepositoryId, Cancel);

        Assert.Equal(MirrorFailure.None, result.Failure);
        Assert.False(File.Exists(Path.Combine(Mirror, "junk")));
        Assert.Equal(_remote.Main, TestGit.Run(_root, $"--git-dir={Mirror}", "rev-parse", "refs/heads/main"));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_mirrors_directory_named_with_a_trailing_separator_works_as_well()
    {
        var options = Options() with { MirrorsDirectory = _mirrors + Path.DirectorySeparatorChar };

        var result = await Mirrors(options).FetchAsync(RepositoryId, Cancel);

        Assert.Equal(MirrorFailure.None, result.Failure);
    }

    [Fact]
    public async Task A_mirrors_directory_that_cannot_be_made_is_a_failure_of_git_and_not_an_exception()
    {
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "a file where the directory should be");
        var options = Options() with { MirrorsDirectory = Path.Combine(blocker, "mirrors") };

        var result = await Mirrors(options).FetchAsync(RepositoryId, Cancel);

        Assert.Equal(MirrorFailure.GitFailed, result.Failure);
    }

    [Fact]
    public async Task An_address_that_is_not_an_absolute_address_is_a_failure_of_git_and_not_an_exception()
    {
        var result = await Mirrors(Options(remote: _ => "acme/web")).FetchAsync(RepositoryId, Cancel);

        Assert.Equal(MirrorFailure.GitFailed, result.Failure);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_history_larger_than_the_bound_is_refused_before_anything_is_written()
    {
        var options = Options(git: Recording()) with { MaximumBundleBytes = 10 };

        var result = await Mirrors(options).CreateBundleAsync(new BundleRequest(RepositoryId, _remote.Main, TaskId, StepRunId), Cancel);

        Assert.Equal(MirrorFailure.TooLarge, result.Failure);
        Assert.DoesNotContain(" bundle create ", Log, StringComparison.Ordinal);
        Assert.DoesNotContain("update-ref refs/hephaestus", Log, StringComparison.Ordinal);
    }

    [Theory(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    [InlineData("fatal: unable to access 'https://x/': The requested URL returned error: 429")]
    [InlineData("fatal: unable to access 'https://x/': The requested URL returned error: 403 (secondary rate limit exceeded)")]
    public async Task A_rate_limit_is_a_wait_and_the_token_is_kept(string said)
    {
        _stores.AppId = 123456;
        _stores.Secrets[GitHubAppCredential.PrivateKeySecretName] = RSA.Create(2048).ExportRSAPrivateKeyPem();
        _stores.Projects.Add(new Project(1, "acme/web", "main", new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero), null));
        var github = new FakeGitHub();
        var mirrors = new GitHubMirrors(_stores, _stores, TimeProvider.System, new GitHubRateLimit(TimeProvider.System), Options(git: Recording($"echo \"{said}\" >&2; exit 128", onlyFetch: true)), () => github);

        await mirrors.FetchAsync(1, Cancel);
        var asked = github.Requests.Count(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal));
        var result = await mirrors.FetchAsync(1, Cancel);

        Assert.Equal(MirrorFailure.Unreachable, result.Failure);
        Assert.True(asked > 0);
        Assert.Equal(asked, github.Requests.Count(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal)));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_token_git_says_GitHub_refused_is_forgotten_so_that_the_next_fetch_asks_for_another()
    {
        _stores.AppId = 123456;
        _stores.Secrets[GitHubAppCredential.PrivateKeySecretName] = RSA.Create(2048).ExportRSAPrivateKeyPem();
        _stores.Projects.Add(new Project(1, "acme/web", "main", new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero), null));
        var github = new FakeGitHub();
        var mirrors = new GitHubMirrors(_stores, _stores, TimeProvider.System, new GitHubRateLimit(TimeProvider.System), Options(git: Recording("echo \"fatal: unable to access 'https://x/': The requested URL returned error: 401\" >&2; exit 128", onlyFetch: true)), () => github);

        var first = await mirrors.FetchAsync(1, Cancel);
        var asked = github.Requests.Count(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal));
        var second = await mirrors.FetchAsync(1, Cancel);

        Assert.Equal(MirrorFailure.Rejected, first.Failure);
        Assert.Equal(MirrorFailure.Rejected, second.Failure);
        Assert.True(github.Requests.Count(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal)) > asked);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task Only_the_proxy_and_certificate_variables_of_the_host_reach_git()
    {
        var names = new[] { "HTTPS_PROXY", "NO_PROXY", "SSL_CERT_FILE", "HEPHAESTUS_TEST_OTHER" };
        var before = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://proxy.test:3128");
            Environment.SetEnvironmentVariable("NO_PROXY", "localhost");
            Environment.SetEnvironmentVariable("SSL_CERT_FILE", "/etc/ssl/certs/test.pem");
            Environment.SetEnvironmentVariable("HEPHAESTUS_TEST_OTHER", "no");
            await Mirrors(Options(git: Recording())).FetchAsync(RepositoryId, Cancel);
        }
        finally
        {
            for (var i = 0; i < names.Length; i++)
            {
                Environment.SetEnvironmentVariable(names[i], before[i]);
            }
        }

        Assert.Contains("HTTPS_PROXY=http://proxy.test:3128", Log, StringComparison.Ordinal);
        Assert.Contains("NO_PROXY=localhost", Log, StringComparison.Ordinal);
        Assert.Contains("SSL_CERT_FILE=/etc/ssl/certs/test.pem", Log, StringComparison.Ordinal);
        Assert.DoesNotContain("HEPHAESTUS_TEST_OTHER", Log, StringComparison.Ordinal);
        Assert.Contains("gc.autoDetach=false", Log, StringComparison.Ordinal);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_fetch_that_is_cancelled_kills_its_git_process_and_the_cancellation_reaches_the_caller()
    {
        var script = Recording("sleep 30", onlyFetch: true);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        var fetching = Mirrors(Options(git: script)).FetchAsync(RepositoryId, source.Token);
        await Task.Delay(500, Cancel);

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetching);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task Git_is_refused_a_directory_that_is_not_in_the_mirrors()
    {
        var git = new GitProcess(Options());
        Directory.CreateDirectory(_mirrors);

        await Assert.ThrowsAsync<InvalidOperationException>(() => git.InMirrorAsync(RepositoryId, [$"--git-dir={_root}", "status"], null, TimeSpan.FromSeconds(5), Cancel));
        await Assert.ThrowsAsync<InvalidOperationException>(() => git.InMirrorAsync(RepositoryId, ["--git-dir=" + Path.Combine(_mirrors, "..", "remote", ".git"), "status"], null, TimeSpan.FromSeconds(5), Cancel));
    }

    [Fact]
    public void A_mirror_is_named_after_the_repository_id_and_nothing_else()
    {
        var git = new GitProcess(Options());

        Assert.Equal(Path.Combine(_mirrors, "42.git"), git.MirrorOf(42));
        Assert.Equal(Path.Combine(_mirrors, "-1.git"), git.MirrorOf(-1));
        Assert.False(git.MirrorExists(42));
    }
}
