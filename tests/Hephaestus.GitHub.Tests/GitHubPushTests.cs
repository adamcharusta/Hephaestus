using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Secrets;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.GitHub.Tests;

/// <summary>
/// Story 3.7, AD-4, AD-9, AD-19, FR-4, FR-20, FR-44, AR-52, AR-53: the gateway pushes the bundle of a checkpoint to the Task branch by fast-forward only, and only after the bundle was imported into a quarantine ref with strict
/// object checking, was found to carry the Task branch alone, descend from the last pushed tip, hold no stored secret value, and touch no protected path. A repository on this machine stands in for GitHub, with real git.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class GitHubPushTests : IDisposable
{
    private const long RepositoryId = 42;
    private const string Branch = "hephaestus/1-task";
    private const string Token = "ghs_push-token-0123456789";
    private const string Secret = "sk-stored-secret-0123456789";
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");

    private readonly string _root = Directory.CreateTempSubdirectory("hephaestus-push-").FullName;
    private readonly FakeStores _stores = new();
    private readonly string _mirrors;
    private readonly string _bundles;
    private readonly (string Path, string Main, string First, string TaskBranchTip) _remote;
    private int _work;

    public GitHubPushTests()
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

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string Mirror => Path.Combine(_mirrors, $"{RepositoryId}.git");

    private string Log => File.ReadAllText(Path.Combine(_root, "git.log"));

    private GitMirrorOptions Options(string? git = null, Func<string, string>? remote = null) =>
        new(_mirrors, _bundles) { AllowedProtocols = "file", RemoteUrl = remote ?? (_ => new Uri(_remote.Path).AbsoluteUri), GitExecutable = git ?? "git", FetchTimeout = TimeSpan.FromSeconds(60), CommandTimeout = TimeSpan.FromSeconds(60) };

    private async Task<GitHubMirrors> GatewayAsync(GitMirrorOptions? options = null, MirrorToken? token = null, bool masker = true)
    {
        SecretMasker? built = null;
        if (masker)
        {
            built = new SecretMasker(_stores);
            await built.RebuildAsync(Cancel);
        }

        return new GitHubMirrors(_stores, options ?? Options(), built, (_, _) => Task.FromResult(token ?? MirrorToken.Of(Token)));
    }

    /// <summary>A stand-in for git that logs what it was started with, runs <paramref name="onPush"/> just before a push, and then runs git.</summary>
    private string Script(string onPush = "", string onImport = "")
    {
        var log = Path.Combine(_root, "git.log");
        var script = Path.Combine(_root, "git-recording");
        File.WriteAllText(script, $"#!/bin/sh\n{{ echo \"ARGS: $*\"; env | sort; echo END; }} >> '{log}'\ncase \" $* \" in *\" push \"*) {(onPush.Length == 0 ? ":" : onPush)};; esac\ncase \" $* \" in *fsckObjects*\" fetch \"*) {(onImport.Length == 0 ? ":" : onImport)};; esac\nexec git \"$@\"\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    private string RemoteTip(string branch = Branch) => TestGit.Run(_remote.Path, "rev-parse", $"refs/heads/{branch}");

    private string MirrorRefs() => TestGit.Run(_root, $"--git-dir={Mirror}", "for-each-ref", "--format=%(refname)", "refs/hephaestus");

    /// <summary>
    /// A checkpoint: a clone of the repository, the Task branch at <paramref name="from"/>, <paramref name="change"/> made and committed, and a bundle of the branch with <paramref name="from"/> as its prerequisite, written under the
    /// name the runtime writes it under.
    /// </summary>
    private BundleReference Checkpoint(Action<string> change, string message = "work", string? from = null, Guid? stepRun = null, string? branch = null, bool orphan = false)
    {
        var parent = from ?? _remote.First;
        var work = Path.Combine(_root, $"work-{_work++}");
        TestGit.Run(_root, "clone", "--quiet", _remote.Path, work);
        if (orphan)
        {
            TestGit.Run(work, "checkout", "--quiet", "--orphan", branch ?? Branch);
            TestGit.Run(work, "rm", "-rf", "--quiet", ".");
        }
        else
        {
            TestGit.Run(work, "checkout", "--quiet", "-B", branch ?? Branch, parent);
        }

        change(work);
        TestGit.Run(work, "add", "-A", "-f");
        TestGit.Run(work, "commit", "--quiet", "--allow-empty", "-m", message);
        var run = stepRun ?? StepRunId;
        var relative = BundleReference.CheckpointPath(TaskId, run);
        var file = Path.Combine(_bundles, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.Delete(file);
        string[] heads = orphan ? [$"refs/heads/{branch ?? Branch}"] : [$"refs/heads/{branch ?? Branch}", $"^{parent}"];
        TestGit.Run(work, ["bundle", "create", "--quiet", file, .. heads]);
        return new BundleReference(relative, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), TestGit.Run(work, "rev-parse", "HEAD"), parent);
    }

    private BundleReference Add(string path, string content = "content\n", string message = "work", string? from = null, Guid? stepRun = null) =>
        Checkpoint(work => Write(work, path, content), message, from, stepRun);

    private static void Write(string work, string path, string content)
    {
        var full = Path.Combine(work, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private PushRequest Request(BundleReference bundle, string? start = null, string? branch = null, Guid? stepRun = null) =>
        new(RepositoryId, TaskId, stepRun ?? StepRunId, branch ?? Branch, bundle, start ?? _remote.First);

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_that_passed_every_check_moves_the_Task_branch_forward_by_fast_forward_and_the_mirror_follows()
    {
        var bundle = Add("src/feature.txt", "a feature\n", "Add the feature");
        var gateway = await GatewayAsync();

        var result = await gateway.PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal(PushOutcome.Pushed, result.Outcome);
        Assert.Equal(bundle.Tip, RemoteTip());
        TestGit.Run(_remote.Path, "merge-base", "--is-ancestor", _remote.First, bundle.Tip);
        Assert.Equal("1", TestGit.Run(_remote.Path, "rev-list", "--count", $"{_remote.First}..{bundle.Tip}"));
        Assert.Equal(bundle.Tip, TestGit.Run(_root, $"--git-dir={Mirror}", "rev-parse", $"refs/heads/{Branch}"));
        Assert.Equal(_remote.Main, TestGit.Run(_remote.Path, "rev-parse", "refs/heads/main"));
        Assert.Equal(string.Empty, MirrorRefs());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_Task_whose_branch_is_not_on_GitHub_yet_gets_it_at_the_tip_when_nothing_was_pushed()
    {
        TestGit.Run(_remote.Path, "branch", "-D", Branch);
        var bundle = Add("a.txt");

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal(PushOutcome.Pushed, result.Outcome);
        Assert.Equal(bundle.Tip, RemoteTip());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_second_push_in_a_row_resolves_as_already_pushed_and_git_is_asked_to_push_once()
    {
        var bundle = Add("a.txt");
        var gateway = await GatewayAsync(Options(git: Script()));
        var request = Request(bundle);

        var first = await gateway.PushAsync(request, "key-1", Cancel);
        var second = await gateway.PushAsync(request, "key-1", Cancel);

        Assert.Equal((PushOutcome.Pushed, PushOutcome.AlreadyPushed), (first.Outcome, second.Outcome));
        Assert.Equal(1, Log.Split('\n').Count(l => l.StartsWith("ARGS:", StringComparison.Ordinal) && l.Contains(" push ", StringComparison.Ordinal)));
        Assert.Equal(bundle.Tip, RemoteTip());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_push_that_was_made_and_whose_bundle_has_since_been_deleted_is_found_on_the_branch_and_nothing_is_pushed()
    {
        var bundle = Add("a.txt");
        var gateway = await GatewayAsync(Options(git: Script()));
        await gateway.PushAsync(Request(bundle), "key-1", Cancel);
        gateway.DeleteBundle(bundle);
        TestGit.Run(_remote.Path, "update-ref", $"refs/heads/{Branch}", RemoteTip());

        var again = await gateway.PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal(PushOutcome.AlreadyPushed, again.Outcome);
        Assert.False(File.Exists(Path.Combine(_bundles, bundle.Path)));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_branch_that_has_moved_on_from_the_tip_after_a_push_that_was_not_recorded_is_still_already_pushed()
    {
        var bundle = Add("a.txt");
        var gateway = await GatewayAsync(Options(git: Script()));
        await gateway.PushAsync(Request(bundle), "key-1", Cancel);
        var other = Path.Combine(_root, "other");
        TestGit.Run(_root, "clone", "--quiet", "--branch", Branch, _remote.Path, other);
        File.WriteAllText(Path.Combine(other, "human.txt"), "a human\n");
        TestGit.Run(other, "add", "-A");
        TestGit.Run(other, "commit", "--quiet", "-m", "by a human");
        TestGit.Run(other, "push", "--quiet", "origin", Branch);

        var again = await gateway.PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal(PushOutcome.AlreadyPushed, again.Outcome);
        Assert.Equal(1, Log.Split('\n').Count(l => l.StartsWith("ARGS:", StringComparison.Ordinal) && l.Contains(" push ", StringComparison.Ordinal)));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_branch_that_someone_else_pushed_to_holds_the_Task_nothing_is_pushed_and_the_commits_are_named()
    {
        var bundle = Add("a.txt");
        var other = Path.Combine(_root, "other");
        TestGit.Run(_root, "clone", "--quiet", "--branch", Branch, _remote.Path, other);
        File.WriteAllText(Path.Combine(other, "human.txt"), "a human\n");
        TestGit.Run(other, "add", "-A");
        TestGit.Run(other, "commit", "--quiet", "-m", "by a human");
        TestGit.Run(other, "push", "--quiet", "origin", Branch);
        var theirs = RemoteTip();

        var result = await (await GatewayAsync(Options(git: Script()))).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushOutcome.Blocked, PushCheck.RemoteMoved, theirs[..12]), (result.Outcome, result.Check, result.Detail));
        Assert.Equal(theirs, RemoteTip());
        Assert.DoesNotContain(" push ", Log, StringComparison.Ordinal);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_branch_that_was_deleted_after_a_push_holds_the_Task_and_has_no_commits_to_name()
    {
        var first = Add("a.txt");
        var gateway = await GatewayAsync();
        await gateway.PushAsync(Request(first), "key-1", Cancel);
        TestGit.Run(_remote.Path, "branch", "-D", Branch);
        var next = Add("b.txt", from: first.Tip, stepRun: Guid.NewGuid());

        var result = await gateway.PushAsync(Request(next), "key-2", Cancel);

        Assert.Equal((PushOutcome.Blocked, PushCheck.RemoteMoved, null), (result.Outcome, result.Check, result.Detail));
        Assert.False(TestGitBranchExists(_remote.Path, Branch));
    }

    private static bool TestGitBranchExists(string repository, string branch) =>
        TestGit.Run(repository, "for-each-ref", "--format=%(refname)", $"refs/heads/{branch}").Length > 0;

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_branch_that_was_rewritten_names_the_commits_since_the_Task_began()
    {
        var bundle = Add("a.txt");
        TestGit.Run(_remote.Path, "update-ref", $"refs/heads/{Branch}", _remote.Main);

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushCheck.RemoteMoved, _remote.Main[..12]), (result.Check, result.Detail));
        Assert.Equal(_remote.Main, RemoteTip());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_branch_that_moved_between_the_fetch_and_the_push_is_refused_by_git_and_reported_as_moved()
    {
        var bundle = Add("a.txt");
        var script = Script($"git -C '{_remote.Path}' update-ref refs/heads/{Branch} {_remote.Main}");

        var result = await (await GatewayAsync(Options(git: script))).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushOutcome.Blocked, PushCheck.RemoteMoved, _remote.Main[..12]), (result.Outcome, result.Check, result.Detail));
        Assert.Equal(_remote.Main, RemoteTip());
        Assert.Equal(string.Empty, MirrorRefs());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_branch_check_says_whether_the_branch_is_where_Hephaestus_left_it()
    {
        var gateway = await GatewayAsync();
        var inSync = await gateway.CheckBranchAsync(new BranchCheckRequest(RepositoryId, Branch, _remote.First, _remote.First), Cancel);
        TestGit.Run(_remote.Path, "branch", "-D", Branch);
        var absentAtTheStart = await gateway.CheckBranchAsync(new BranchCheckRequest(RepositoryId, Branch, _remote.First, _remote.First), Cancel);
        var absentAfterAPush = await gateway.CheckBranchAsync(new BranchCheckRequest(RepositoryId, Branch, _remote.Main, _remote.First), Cancel);
        TestGit.Run(_remote.Path, "branch", Branch, _remote.Main);
        var moved = await gateway.CheckBranchAsync(new BranchCheckRequest(RepositoryId, Branch, _remote.First, _remote.First), Cancel);

        Assert.Equal(BranchState.InSync, inSync.State);
        Assert.Equal(BranchState.InSync, absentAtTheStart.State);
        Assert.Equal((BranchState.Moved, null), (absentAfterAPush.State, absentAfterAPush.Detail));
        Assert.Equal((BranchState.Moved, _remote.Main[..12]), (moved.State, moved.Detail));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_branch_check_names_at_most_ten_commits_and_says_there_are_more()
    {
        var other = Path.Combine(_root, "other");
        TestGit.Run(_root, "clone", "--quiet", "--branch", Branch, _remote.Path, other);
        for (var index = 0; index < 12; index++)
        {
            File.WriteAllText(Path.Combine(other, $"f{index}.txt"), "x\n");
            TestGit.Run(other, "add", "-A");
            TestGit.Run(other, "commit", "--quiet", "-m", $"c{index}");
        }

        TestGit.Run(other, "push", "--quiet", "origin", Branch);

        var result = await (await GatewayAsync()).CheckBranchAsync(new BranchCheckRequest(RepositoryId, Branch, _remote.First, _remote.First), Cancel);

        Assert.Equal(BranchState.Moved, result.State);
        Assert.Equal(11, result.Detail!.Split(", ").Length);
        Assert.EndsWith(", …", result.Detail, StringComparison.Ordinal);
        Assert.StartsWith(RemoteTip()[..12], result.Detail, StringComparison.Ordinal);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_branch_check_of_a_project_that_is_not_registered_or_cannot_be_fetched_says_why()
    {
        var unknown = await (await GatewayAsync()).CheckBranchAsync(new BranchCheckRequest(7, Branch, _remote.First, _remote.First), Cancel);
        var rejected = await (await GatewayAsync(token: MirrorToken.Failed(MirrorFailure.Rejected))).CheckBranchAsync(new BranchCheckRequest(RepositoryId, Branch, _remote.First, _remote.First), Cancel);

        Assert.Equal((BranchState.Failed, MirrorFailure.UnknownProject), (unknown.State, unknown.Failure));
        Assert.Equal((BranchState.Failed, MirrorFailure.Rejected), (rejected.State, rejected.Failure));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_whose_file_is_not_the_one_that_was_written_is_not_imported()
    {
        var bundle = Add("a.txt");
        var wrong = new BundleReference(bundle.Path, new string('0', 64), bundle.Tip, bundle.Prerequisite);

        var result = await (await GatewayAsync()).PushAsync(Request(wrong), "key-1", Cancel);

        Assert.Equal((PushOutcome.Blocked, PushCheck.Import, "bundle-file-changed"), (result.Outcome, result.Check, result.Detail));
        Assert.Equal(_remote.First, RemoteTip());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_whose_file_is_gone_or_is_a_link_is_not_imported()
    {
        var bundle = Add("a.txt");
        var file = Path.Combine(_bundles, bundle.Path);
        var gateway = await GatewayAsync();
        var moved = file + ".real";
        File.Move(file, moved);

        var missing = await gateway.PushAsync(Request(bundle), "key-1", Cancel);
        File.CreateSymbolicLink(file, moved);
        var link = await gateway.PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushCheck.Import, "bundle-file-missing"), (missing.Check, missing.Detail));
        Assert.Equal((PushCheck.Import, "bundle-file-missing"), (link.Check, link.Detail));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_over_the_bound_is_not_imported()
    {
        var bundle = Add("a.txt");

        var result = await (await GatewayAsync(Options() with { MaximumBundleBytes = 10 })).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushCheck.Import, "bundle-file-too-large"), (result.Check, result.Detail));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_that_carries_a_ref_that_is_not_the_Task_branch_is_refused_and_so_is_one_with_two_heads()
    {
        var other = Checkpoint(work => Write(work, "a.txt", "x\n"), branch: "hephaestus/2-other");
        var two = Checkpoint(work => Write(work, "a.txt", "x\n"), stepRun: Guid.NewGuid());
        var twoFile = Path.Combine(_bundles, two.Path);
        var twoWork = Path.Combine(_root, $"work-{_work - 1}");
        TestGit.Run(twoWork, "branch", "hephaestus/3-extra");
        TestGit.Run(twoWork, "bundle", "create", "--quiet", twoFile, $"refs/heads/{Branch}", "refs/heads/hephaestus/3-extra", $"^{_remote.First}");
        var twoHeads = new BundleReference(two.Path, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(twoFile))), two.Tip, two.Prerequisite);
        var gateway = await GatewayAsync();

        var wrongName = await gateway.PushAsync(Request(other), "key-1", Cancel);
        var severalHeads = await gateway.PushAsync(Request(twoHeads, stepRun: Guid.NewGuid()), "key-2", Cancel);

        Assert.Equal((PushCheck.Branch, "bundle-other-ref"), (wrongName.Check, wrongName.Detail));
        Assert.Equal((PushOutcome.Blocked, PushCheck.Branch, "bundle-heads"), (severalHeads.Outcome, severalHeads.Check, severalHeads.Detail));
        Assert.Equal(_remote.First, RemoteTip());
        Assert.False(TestGitBranchExists(_remote.Path, "hephaestus/2-other"));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_whose_head_is_not_the_tip_it_was_said_to_carry_is_refused()
    {
        var bundle = Add("a.txt");
        var other = new BundleReference(bundle.Path, bundle.Sha256, _remote.Main, bundle.Prerequisite);

        var result = await (await GatewayAsync()).PushAsync(Request(other), "key-1", Cancel);

        Assert.Equal((PushCheck.Import, "bundle-tip"), (result.Check, result.Detail));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_that_is_not_a_bundle_is_refused()
    {
        var bundle = Add("a.txt");
        var file = Path.Combine(_bundles, bundle.Path);
        File.WriteAllText(file, "not a bundle\n");
        var forged = new BundleReference(bundle.Path, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), bundle.Tip, bundle.Prerequisite);

        var result = await (await GatewayAsync()).PushAsync(Request(forged), "key-1", Cancel);

        Assert.Equal((PushCheck.Import, "bundle-unreadable"), (result.Check, result.Detail));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task An_object_that_strict_checking_refuses_stops_the_import_and_nothing_is_pushed()
    {
        var bundle = Checkpoint(work =>
        {
            Write(work, "a.txt", "x\n");
            var tree = TestGit.Run(work, "write-tree");
            var parent = TestGit.Run(work, "rev-parse", "HEAD");
            var bad = HashObject(work, $"tree {tree}\nparent {parent}\n\nno author\n");
            TestGit.Run(work, "update-ref", $"refs/heads/{Branch}", bad);
            TestGit.Run(work, "reset", "--quiet", "--soft", bad);
        });
        var gateway = await GatewayAsync();

        var result = await gateway.PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushOutcome.Blocked, PushCheck.Import), (result.Outcome, result.Check));
        Assert.StartsWith("object-check-failed", result.Detail, StringComparison.Ordinal);
        Assert.Equal(_remote.First, RemoteTip());
        Assert.Equal(string.Empty, MirrorRefs());
    }

    private static string HashObject(string work, string content)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = work, RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in new[] { "hash-object", "-t", "commit", "--literally", "-w", "--stdin" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardInput.Write(content);
        process.StandardInput.Close();
        var id = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return id;
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_that_is_not_a_fast_forward_of_the_last_pushed_tip_is_refused()
    {
        var bundle = Checkpoint(work => Write(work, "a.txt", "x\n"), orphan: true);

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushOutcome.Blocked, PushCheck.FastForward), (result.Outcome, result.Check));
        Assert.Equal(_remote.First, RemoteTip());
        Assert.Equal(string.Empty, MirrorRefs());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_bundle_that_adds_nothing_to_the_last_pushed_tip_is_not_a_push()
    {
        // The branch is not on GitHub and nothing was pushed, so the last pushed tip is the start commit, and the bundle's one head is that commit.
        TestGit.Run(_remote.Path, "branch", "-D", Branch);
        var file = Path.Combine(_bundles, BundleReference.CheckpointPath(TaskId, StepRunId));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var work = Path.Combine(_root, "work-whole");
        TestGit.Run(_root, "clone", "--quiet", _remote.Path, work);
        TestGit.Run(work, "checkout", "--quiet", "-B", Branch, _remote.First);
        TestGit.Run(work, "bundle", "create", "--quiet", file, $"refs/heads/{Branch}");
        var bundle = new BundleReference(BundleReference.CheckpointPath(TaskId, StepRunId), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), _remote.First, _remote.First);

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushOutcome.Blocked, PushCheck.FastForward), (result.Outcome, result.Check));
        Assert.False(TestGitBranchExists(_remote.Path, Branch));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_stale_quarantine_name_that_a_crash_left_for_the_step_run_does_not_make_a_new_checkpoint_of_it_a_non_fast_forward()
    {
        var gateway = await GatewayAsync();
        var crashed = Add("a.txt", message: "first checkpoint of the step run");
        await gateway.PushAsync(Request(crashed), "key-0", Cancel);
        TestGit.Run(_remote.Path, "branch", "-f", Branch, _remote.First);
        TestGit.Run(_root, $"--git-dir={Mirror}", "update-ref", $"refs/hephaestus/quarantine/{StepRunId}", crashed.Tip);
        TestGit.Run(_root, $"--git-dir={Mirror}", "update-ref", $"refs/heads/{Branch}", _remote.First);
        var retaken = Add("b.txt", message: "second checkpoint of the same step run");

        var result = await gateway.PushAsync(Request(retaken), "key-1", Cancel);

        Assert.Equal(PushOutcome.Pushed, result.Outcome);
        Assert.Equal(retaken.Tip, RemoteTip());
        Assert.Equal(string.Empty, MirrorRefs());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_file_name_with_a_carriage_return_in_it_is_scanned_and_pushed_like_any_other()
    {
        var bundle = Add("odd\rname.txt");

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal(PushOutcome.Pushed, result.Outcome);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task No_more_than_five_secrets_and_five_protected_paths_are_named_and_the_rest_is_not_looked_for()
    {
        _stores.Secrets["agent"] = Secret;
        var leaks = Checkpoint(work =>
        {
            for (var index = 0; index < 8; index++)
            {
                Write(work, $"leak{index}.txt", Secret + index + "\n");
            }
        });
        var workflows = Checkpoint(work =>
        {
            for (var index = 0; index < 8; index++)
            {
                Write(work, $".github/w{index}.yml", "x\n");
            }
        }, stepRun: Guid.NewGuid());
        var gateway = await GatewayAsync();

        var secrets = await gateway.PushAsync(Request(leaks), "key-1", Cancel);
        var paths = await gateway.PushAsync(Request(workflows, stepRun: Guid.NewGuid()), "key-2", Cancel);

        Assert.Equal(PushCheck.Secret, secrets.Check);
        Assert.Equal(5, secrets.Detail!.Split(", ").Length);
        Assert.Equal(PushCheck.ProtectedPath, paths.Check);
        Assert.Equal(5, paths.Detail!.Split(", ").Length);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task What_git_said_about_a_bundle_it_would_not_import_does_not_give_the_hosts_paths_away()
    {
        var bundle = Add("a.txt");
        var script = Script(onImport: "echo \"fatal: cannot read $*\" >&2; exit 128");

        var result = await (await GatewayAsync(Options(git: script))).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushOutcome.Blocked, PushCheck.Import), (result.Outcome, result.Check));
        Assert.StartsWith("object-check-failed: fatal: cannot read", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(_bundles, result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(_mirrors, result.Detail, StringComparison.Ordinal);
    }

    [Theory(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    [InlineData("plain")]
    [InlineData("base64")]
    [InlineData("url")]
    public async Task A_stored_secret_value_in_a_new_file_in_any_form_blocks_the_push_and_the_detail_names_the_path_and_not_the_value(string form)
    {
        _stores.Secrets["agent"] = Secret + "/+=";
        var written = form switch
        {
            "plain" => Secret + "/+=",
            "base64" => Convert.ToBase64String(Encoding.UTF8.GetBytes(Secret + "/+=")),
            _ => Uri.EscapeDataString(Secret + "/+="),
        };
        var bundle = Add("config/settings.txt", $"key = {written}\n");

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushOutcome.Blocked, PushCheck.Secret, "config/settings.txt"), (result.Outcome, result.Check, result.Detail));
        Assert.Equal(_remote.First, RemoteTip());
        Assert.Equal(string.Empty, MirrorRefs());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_stored_secret_value_in_a_commit_message_blocks_the_push()
    {
        _stores.Secrets["agent"] = Secret;
        var bundle = Add("a.txt", message: $"Use the key {Secret} for now");

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushCheck.Secret, $"commit {bundle.Tip[..12]}"), (result.Check, result.Detail));
        Assert.DoesNotContain(Secret, result.Detail, StringComparison.Ordinal);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_stored_secret_value_in_a_file_name_blocks_the_push_and_what_is_said_does_not_hold_the_name()
    {
        _stores.Secrets["agent"] = Secret;
        var bundle = Add($"notes-{Secret}.txt");

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal(PushCheck.Secret, result.Check);
        Assert.StartsWith("tree ", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.Detail, StringComparison.Ordinal);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_secret_that_an_earlier_commit_of_the_bundle_holds_and_a_later_one_removed_still_blocks_the_push()
    {
        _stores.Secrets["agent"] = Secret;
        var bundle = Checkpoint(work =>
        {
            Write(work, "leak.txt", Secret + "\n");
            TestGit.Run(work, "add", "-A");
            TestGit.Run(work, "commit", "--quiet", "-m", "oops");
            File.Delete(Path.Combine(work, "leak.txt"));
        });

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushCheck.Secret, "leak.txt"), (result.Check, result.Detail));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_large_file_is_scanned_through_and_a_value_across_the_chunks_of_it_is_found()
    {
        _stores.Secrets["agent"] = Secret;
        var padding = new string('x', 200_000);
        var bundle = Add("big.txt", padding + Secret + padding);

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushCheck.Secret, "big.txt"), (result.Check, result.Detail));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_binary_file_and_text_that_only_looks_like_a_secret_do_not_block_the_push()
    {
        _stores.Secrets["agent"] = Secret;
        var bundle = Checkpoint(work =>
        {
            File.WriteAllBytes(Path.Combine(work, "image.bin"), [.. Enumerable.Range(0, 5000).Select(i => (byte)(i * 31))]);
            Write(work, "close.txt", "sk-stored-secret-012345678\n");
        });

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal(PushOutcome.Pushed, result.Outcome);
    }

    [Theory(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData(".github/CODEOWNERS")]
    [InlineData(".GITHUB/workflows/x.yml")]
    [InlineData("hephaestus.yml")]
    [InlineData("Hephaestus.yml")]
    public async Task A_change_to_a_protected_path_blocks_the_push_and_the_detail_names_it(string path)
    {
        var bundle = Add(path, "x: 1\n");

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushOutcome.Blocked, PushCheck.ProtectedPath, path), (result.Outcome, result.Check, result.Detail));
        Assert.Equal(_remote.First, RemoteTip());
        Assert.Equal(string.Empty, MirrorRefs());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_path_the_Owner_added_to_the_list_is_protected_and_one_that_was_taken_off_is_not()
    {
        _stores.ProtectedPaths.Remove("hephaestus.yml");
        _stores.ProtectedPaths.Add("deploy/**");
        var gateway = await GatewayAsync();

        var deploy = await gateway.PushAsync(Request(Add("deploy/prod.yml", "x\n"), stepRun: StepRunId), "key-1", Cancel);
        var config = await gateway.PushAsync(Request(Add("hephaestus.yml", "x\n", from: _remote.First)), "key-2", Cancel);

        Assert.Equal((PushCheck.ProtectedPath, "deploy/prod.yml"), (deploy.Check, deploy.Detail));
        Assert.Equal(PushOutcome.Pushed, config.Outcome);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_rename_into_a_protected_path_and_a_deletion_of_a_protected_file_are_both_caught()
    {
        TestGit.Run(_remote.Path, "checkout", "--quiet", "-B", "scratch", _remote.First);
        Directory.CreateDirectory(Path.Combine(_remote.Path, ".github"));
        File.WriteAllText(Path.Combine(_remote.Path, ".github", "CODEOWNERS"), "* @owner\n");
        TestGit.Run(_remote.Path, "add", "-A");
        TestGit.Run(_remote.Path, "commit", "--quiet", "-m", "protected file");
        var start = TestGit.Run(_remote.Path, "rev-parse", "HEAD");
        TestGit.Run(_remote.Path, "branch", "-f", Branch, start);
        TestGit.Run(_remote.Path, "checkout", "--quiet", "main");
        var deletion = Checkpoint(work => File.Delete(Path.Combine(work, ".github", "CODEOWNERS")), from: start);
        var gateway = await GatewayAsync();

        var deleted = await gateway.PushAsync(Request(deletion, start), "key-1", Cancel);

        Assert.Equal((PushCheck.ProtectedPath, ".github/CODEOWNERS"), (deleted.Check, deleted.Detail));
        var renamed = Checkpoint(work => File.Move(Path.Combine(work, "README.md"), Path.Combine(work, ".github", "README.md")), from: start, stepRun: Guid.NewGuid());
        var renamedResult = await gateway.PushAsync(Request(renamed, start, stepRun: Guid.NewGuid()), "key-2", Cancel);
        Assert.Equal(PushCheck.ProtectedPath, renamedResult.Check);
        Assert.Contains(".github/README.md", renamedResult.Detail, StringComparison.Ordinal);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_diff_is_the_whole_checkpoint_against_the_start_so_a_protected_file_that_a_commit_added_and_a_later_one_removed_is_not_in_it()
    {
        var bundle = Checkpoint(work =>
        {
            Write(work, ".github/x.yml", "x\n");
            TestGit.Run(work, "add", "-A", "-f");
            TestGit.Run(work, "commit", "--quiet", "-m", "add");
            File.Delete(Path.Combine(work, ".github", "x.yml"));
            Write(work, "ok.txt", "ok\n");
        });

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal(PushOutcome.Pushed, result.Outcome);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_secret_is_looked_for_before_the_protected_paths()
    {
        _stores.Secrets["agent"] = Secret;
        var bundle = Checkpoint(work => Write(work, ".github/x.yml", Secret + "\n"));

        var result = await (await GatewayAsync()).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal(PushCheck.Secret, result.Check);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_push_is_one_refspec_from_the_quarantine_ref_to_the_Task_branch_with_no_plus_no_force_and_no_delete()
    {
        var bundle = Add("a.txt");

        await (await GatewayAsync(Options(git: Script()))).PushAsync(Request(bundle), "key-1", Cancel);

        var push = Log.Split('\n').Single(l => l.StartsWith("ARGS:", StringComparison.Ordinal) && l.Contains(" push ", StringComparison.Ordinal));
        Assert.EndsWith($" push --porcelain --no-follow-tags {new Uri(_remote.Path).AbsoluteUri} refs/hephaestus/quarantine/{StepRunId}:refs/heads/{Branch}", push, StringComparison.Ordinal);
        Assert.DoesNotContain("--force", push, StringComparison.Ordinal);
        Assert.DoesNotContain("--delete", push, StringComparison.Ordinal);
        Assert.DoesNotContain("--mirror", push, StringComparison.Ordinal);
        Assert.DoesNotContain(" +", push, StringComparison.Ordinal);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_token_is_in_the_environment_of_the_pushing_process_and_in_no_argument_and_the_import_names_only_the_local_protocol()
    {
        var bundle = Add("a.txt");

        await (await GatewayAsync(Options(git: Script()))).PushAsync(Request(bundle), "key-1", Cancel);

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + Token));
        Assert.Contains($"GIT_CONFIG_VALUE_0=Authorization: Basic {basic}", Log, StringComparison.Ordinal);
        foreach (var line in Log.Split('\n').Where(l => l.StartsWith("ARGS:", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(Token, line, StringComparison.Ordinal);
            Assert.DoesNotContain(basic, line, StringComparison.Ordinal);
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Where(f => !f.StartsWith(Path.Combine(_root, "remote") + "/", StringComparison.Ordinal) && !f.Contains("/work-", StringComparison.Ordinal) && !f.EndsWith("git.log", StringComparison.Ordinal)))
        {
            Assert.False(File.ReadAllBytes(file).AsSpan().IndexOf(Encoding.UTF8.GetBytes(Token)) >= 0, file);
            Assert.False(File.ReadAllBytes(file).AsSpan().IndexOf(Encoding.UTF8.GetBytes(basic)) >= 0, file);
        }
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task The_import_alone_is_given_the_file_protocol_and_every_other_command_has_only_the_configured_ones()
    {
        var bundle = Add("a.txt");

        await (await GatewayAsync(Options(git: Script()) with { AllowedProtocols = "file:http" })).PushAsync(Request(bundle), "key-1", Cancel);

        var runs = Log.Split("END\n", StringSplitOptions.RemoveEmptyEntries);
        var import = runs.Single(run => run.Contains(" fetch ", StringComparison.Ordinal) && run.Contains("fsckObjects", StringComparison.Ordinal));
        Assert.Contains("GIT_ALLOW_PROTOCOL=file\n", import, StringComparison.Ordinal);
        Assert.Contains("-c transfer.fsckObjects=true", import, StringComparison.Ordinal);
        Assert.All(runs.Where(run => !ReferenceEquals(run, import)), run => Assert.Contains("GIT_ALLOW_PROTOCOL=file:http", run, StringComparison.Ordinal));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_gateway_built_without_the_masker_pushes_nothing()
    {
        var bundle = Add("a.txt");
        var gateway = await GatewayAsync(masker: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.PushAsync(Request(bundle), "key-1", Cancel));

        Assert.Equal(_remote.First, RemoteTip());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_push_needs_a_key_and_a_request()
    {
        var gateway = await GatewayAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => gateway.PushAsync(null!, "key-1", Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => gateway.PushAsync(Request(Add("a.txt")), " ", Cancel));
        await Assert.ThrowsAsync<ArgumentNullException>(() => gateway.CheckBranchAsync(null!, Cancel));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_Project_that_is_not_registered_gets_no_push_and_what_the_token_lookup_reports_is_what_the_push_reports()
    {
        var bundle = Add("a.txt");
        var unknown = await (await GatewayAsync()).PushAsync(new PushRequest(7, TaskId, StepRunId, Branch, bundle, _remote.First), "key-1", Cancel);
        var rejected = await (await GatewayAsync(token: MirrorToken.Failed(MirrorFailure.NotInstalled))).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushOutcome.Failed, MirrorFailure.UnknownProject), (unknown.Outcome, unknown.Failure));
        Assert.Equal((PushOutcome.Failed, MirrorFailure.NotInstalled), (rejected.Outcome, rejected.Failure));
        Assert.Equal(_remote.First, RemoteTip());
    }

    [Theory(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    [InlineData("remote: error: GH013: Repository rule violations found for refs/heads/hephaestus/1-task.", MirrorFailure.Rejected)]
    [InlineData("fatal: unable to access 'https://x/': The requested URL returned error: 403", MirrorFailure.Rejected)]
    [InlineData("fatal: unable to access 'https://x/': Could not resolve host: x", MirrorFailure.Unreachable)]
    public async Task What_git_says_when_the_push_itself_fails_decides_whether_it_is_retried_or_refused_and_is_masked(string said, MirrorFailure expected)
    {
        var bundle = Add("a.txt");
        var script = Script($"echo \"{said} {Token} {Secret}\" >&2; exit 1");
        _stores.Secrets["agent"] = Secret;

        var result = await (await GatewayAsync(Options(git: script))).PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal((PushOutcome.Failed, expected), (result.Outcome, result.Failure));
        Assert.DoesNotContain(Token, result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.Detail, StringComparison.Ordinal);
        Assert.Equal(_remote.First, RemoteTip());
        Assert.Equal(string.Empty, MirrorRefs());
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_remote_that_cannot_be_fetched_is_a_failure_and_not_a_check()
    {
        var bundle = Add("a.txt");
        var gateway = await GatewayAsync(Options(remote: _ => new Uri(Path.Combine(_root, "nowhere")).AbsoluteUri));

        var result = await gateway.PushAsync(Request(bundle), "key-1", Cancel);

        Assert.Equal(PushOutcome.Failed, result.Outcome);
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task Deleting_a_bundle_removes_its_file_and_only_that_and_a_file_that_is_not_there_is_no_error()
    {
        var bundle = Add("a.txt");
        var other = Checkpoint(work => Write(work, "b.txt", "b\n"), stepRun: Guid.NewGuid());
        var gateway = await GatewayAsync();

        gateway.DeleteBundle(bundle);
        gateway.DeleteBundle(bundle);

        Assert.False(File.Exists(Path.Combine(_bundles, bundle.Path)));
        Assert.True(File.Exists(Path.Combine(_bundles, other.Path)));
        Assert.Throws<ArgumentNullException>(() => gateway.DeleteBundle(null!));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task Two_pushes_for_the_same_Project_made_together_are_made_one_after_the_other()
    {
        var first = Add("a.txt");
        var second = Add("b.txt", from: _remote.First, stepRun: Guid.NewGuid());
        var gateway = await GatewayAsync();

        var results = await Task.WhenAll(
            gateway.PushAsync(Request(first), "key-1", Cancel),
            gateway.PushAsync(Request(second, stepRun: Guid.NewGuid()), "key-2", Cancel));

        Assert.Equal(1, results.Count(r => r.Outcome == PushOutcome.Pushed));
    }

    [Fact(Skip = "No git on this machine.", SkipUnless = nameof(TestGit.Available), SkipType = typeof(TestGit))]
    public async Task A_start_commit_the_repository_does_not_have_is_missing_and_nothing_is_pushed()
    {
        var bundle = Add("a.txt");

        var result = await (await GatewayAsync()).PushAsync(Request(bundle, start: new string('e', 40)), "key-1", Cancel);

        Assert.Equal((PushOutcome.Failed, MirrorFailure.CommitMissing), (result.Outcome, result.Failure));
        Assert.Equal(_remote.First, RemoteTip());
    }
}
