using System.Runtime.Versioning;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.6, AD-9, FR-20, AR-50: what the checkpoint container runs, run here with the git and the shell of this machine against a real workspace and a real bundle. One commit comes out whose parent is the last pushed
/// tip, the Agent's own commits are flattened into it, a tree that is the tip's makes no commit and no bundle, and a <c>.git</c> that an Agent left in the workspace is never used and nothing in it runs.
/// <see cref="CheckpointEngineTests"/> runs the same script in the tools image.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class CheckpointScriptTests : IDisposable
{
    private const string Branch = "hephaestus/12-checkpoint";
    private const string Name = "hephaestus-test[bot]";
    private const string Email = "hephaestus-test[bot]@users.noreply.github.com";

    private static readonly CheckpointCommit Commit = CheckpointCommit.Compose(
        new CommitIdentity(Name, Email), Language.English, StepKind.Implementation, Guid.Parse("018f4b6e-0000-7000-8000-000000000001"), Guid.Parse("018f4b6e-0000-7000-8000-000000000002"), Agent.ClaudeCode, "opus5.5", Effort.High);

    private readonly string _root = Directory.CreateTempSubdirectory("hephaestus-checkpoint-").FullName;
    private readonly string _source;
    private readonly string _workspace;
    private readonly string _base;
    private readonly string _gitDirectory;
    private readonly string _result;
    private readonly string _tip;
    private readonly string _marker;

    public CheckpointScriptTests()
    {
        _source = Path.Combine(_root, "source");
        _workspace = Path.Combine(_root, "workspace");
        _base = Path.Combine(_root, "base.bundle");
        _gitDirectory = Path.Combine(_root, "repository");
        _result = Path.Combine(_root, "result.bundle");
        _marker = Path.Combine(_root, "EXECUTED");
        _tip = LocalGit.Available ? LocalGit.Repository(_source) : new string('0', 40);
        if (LocalGit.Available)
        {
            LocalGit.Bundle(_source, _base, "main");
            // The workspace as a checkout leaves it: a clone of the tip on the Task branch, owned by the step user.
            LocalGit.Git(_root, "clone", "--quiet", _source, _workspace);
            LocalGit.Git(_workspace, "checkout", "--quiet", "-b", Branch);
        }
    }

    public void Dispose()
    {
        LocalGit.Run(_root, "chmod", ["-R", "u+rwX", _root]);
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private (int ExitCode, string Output) Run(string? bundle = null, string? tip = null, string? workspace = null) =>
        LocalGit.Run(
            _root,
            "sh",
            ["-c", CheckpointScript.Text, "checkpoint", workspace ?? _workspace, bundle ?? _base, _gitDirectory, _result],
            new Dictionary<string, string>
            {
                ["HEPHAESTUS_BRANCH"] = Branch,
                ["HEPHAESTUS_TIP"] = tip ?? _tip,
                ["HEPHAESTUS_NAME"] = Commit.Identity.Name,
                ["HEPHAESTUS_EMAIL"] = Commit.Identity.Email,
                ["HEPHAESTUS_MESSAGE"] = Commit.Message,
                ["HOME"] = _root,
            });

    private string Workspace(params string[] arguments) => LocalGit.Git(_workspace, arguments);

    /// <summary>What a consumer of the bundle sees: the checkpoint commit, fetched into the repository the base came from.</summary>
    private string Fetched(string format)
    {
        LocalGit.Git(_source, "fetch", "--quiet", _result, $"refs/heads/{Branch}:refs/heads/checkpoint");
        return LocalGit.Git(_source, "log", "-1", $"--format={format}", "checkpoint");
    }

    private void AgentWorks()
    {
        File.AppendAllText(Path.Combine(_workspace, "README.md"), "edited\n");
        File.WriteAllText(Path.Combine(_workspace, "added.txt"), "added\n");
        File.Delete(Path.Combine(_workspace, "src", "app.txt"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_changed_tree_is_one_commit_whose_parent_is_the_last_pushed_tip_and_whose_tree_is_the_work_tree()
    {
        AgentWorks();

        var (exit, output) = Run();

        Assert.True(exit == 0, output);
        Assert.Equal(_tip, Fetched("%P"));
        Assert.Equal([".gitignore", "README.md", "added.txt"], LocalGit.Git(_source, "ls-tree", "-r", "--name-only", "checkpoint").Split('\n').Order(StringComparer.Ordinal));
        Assert.Equal("hello\nedited", LocalGit.Git(_source, "show", "checkpoint:README.md"));
        Assert.Equal("added", LocalGit.Git(_source, "show", "checkpoint:added.txt"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void The_commits_an_agent_made_are_flattened_into_the_one_commit()
    {
        File.WriteAllText(Path.Combine(_workspace, "first.txt"), "one\n");
        Workspace("add", "-A");
        Workspace("commit", "--quiet", "-m", "agent commit one");
        File.WriteAllText(Path.Combine(_workspace, "second.txt"), "two\n");
        Workspace("add", "-A");
        Workspace("commit", "--quiet", "-m", "agent commit two");
        File.WriteAllText(Path.Combine(_workspace, "third.txt"), "uncommitted\n");

        var (exit, output) = Run();

        Assert.True(exit == 0, output);
        Assert.Equal(_tip, Fetched("%P"));
        Assert.Equal("1", LocalGit.Git(_source, "rev-list", "--count", $"{_tip}..checkpoint"));
        Assert.Equal(["first.txt", "second.txt", "third.txt"], LocalGit.Git(_source, "diff", "--name-only", _tip, "checkpoint").Split('\n'));
        Assert.DoesNotContain("agent commit", LocalGit.Git(_source, "log", "--format=%B", $"{_tip}..checkpoint"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void The_bundle_has_one_head_the_task_branch_and_one_prerequisite_the_tip()
    {
        AgentWorks();

        Assert.True(Run().ExitCode == 0);

        using var stream = File.OpenRead(_result);
        var header = BundleHeader.TryRead(stream);
        Assert.NotNull(header);
        var head = Assert.Single(header.Heads);
        Assert.Equal($"refs/heads/{Branch}", head.Reference);
        Assert.Equal(_tip, Assert.Single(header.Prerequisites));
        Assert.Equal(Fetched("%H"), head.Commit);
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void The_message_and_the_identity_are_the_ones_core_composed_and_nothing_else()
    {
        AgentWorks();

        Assert.True(Run().ExitCode == 0);

        Assert.Equal($"{Name}|{Email}|{Name}|{Email}", Fetched("%an|%ae|%cn|%ce"));
        Assert.Equal(Commit.Message, LocalGit.Git(_source, "log", "-1", "--format=%B", "checkpoint") + "\n");
        var trailers = LocalGit.Git(_source, "log", "-1", "--format=%(trailers:only,unfold)", "checkpoint").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(':')[0]);
        Assert.Equal(["Hephaestus-Task", "Hephaestus-Step", "Hephaestus-Agent", "Hephaestus-Model", "Hephaestus-Effort"], trailers);
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_tree_that_is_the_tips_makes_no_commit_and_no_bundle()
    {
        var (exit, output) = Run();

        Assert.True(exit == CheckpointScript.Unchanged, output);
        Assert.False(File.Exists(_result));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_tree_the_agent_changed_and_changed_back_makes_no_commit_either()
    {
        File.AppendAllText(Path.Combine(_workspace, "README.md"), "edited\n");
        Workspace("commit", "--quiet", "-am", "agent commit");
        Workspace("reset", "--quiet", "--hard", _tip);
        File.WriteAllText(Path.Combine(_workspace, "README.md"), "hello\n");

        var (exit, _) = Run();

        Assert.Equal(CheckpointScript.Unchanged, exit);
        Assert.False(File.Exists(_result));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_git_directory_with_hostile_configuration_and_hooks_is_never_used_and_nothing_in_it_runs()
    {
        AgentWorks();
        var hook = $"#!/bin/sh\ntouch '{_marker}'\n";
        var hooks = Path.Combine(_workspace, ".git", "hooks");
        Directory.CreateDirectory(hooks);
        foreach (var name in new[] { "pre-commit", "post-commit", "reference-transaction", "pre-push", "post-checkout", "fsmonitor-watchman" })
        {
            File.WriteAllText(Path.Combine(hooks, name), hook);
            File.SetUnixFileMode(Path.Combine(hooks, name), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Workspace("config", "core.fsmonitor", $"touch '{_marker}'; echo");
        Workspace("config", "core.hooksPath", hooks);
        Workspace("config", "core.sshCommand", $"touch '{_marker}'");
        Workspace("config", "core.editor", $"touch '{_marker}'");
        Workspace("config", "core.pager", $"touch '{_marker}'");
        Workspace("config", "core.excludesFile", Path.Combine(_root, "nothing"));
        Workspace("config", "filter.evil.clean", $"touch '{_marker}'; cat");
        Workspace("config", "diff.evil.textconv", $"touch '{_marker}'; cat");
        File.WriteAllText(Path.Combine(_workspace, ".gitattributes"), "* filter=evil diff=evil\n");
        File.WriteAllText(Path.Combine(_workspace, ".git", "info", "exclude"), "added.txt\n");

        var (exit, output) = Run();

        Assert.True(exit == 0, output);
        Assert.False(File.Exists(_marker));
        Assert.Equal(_tip, Fetched("%P"));
        // Its exclude file is not read: the file it names is committed. Its attributes file is the workspace's own, and its filter has no driver in the configuration the script uses.
        Assert.Equal("added", LocalGit.Git(_source, "show", "checkpoint:added.txt"));
        Assert.DoesNotContain(".git/", LocalGit.Git(_source, "ls-tree", "-r", "--name-only", "checkpoint"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_git_file_that_points_somewhere_else_and_a_broken_git_directory_change_nothing()
    {
        AgentWorks();
        Directory.Delete(Path.Combine(_workspace, ".git"), recursive: true);
        File.WriteAllText(Path.Combine(_workspace, ".git"), $"gitdir: {Path.Combine(_root, "elsewhere")}\n");

        var (exit, output) = Run();

        Assert.True(exit == 0, output);
        Assert.Equal(_tip, Fetched("%P"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void The_workspace_is_only_read_and_its_git_directory_is_left_as_it_was()
    {
        AgentWorks();
        var before = Snapshot();
        LocalGit.Run(_root, "chmod", ["-R", "a-w", _workspace]);

        var (exit, output) = Run();
        LocalGit.Run(_root, "chmod", ["-R", "u+rwX", _workspace]);

        Assert.True(exit == 0, output);
        Assert.Equal(before, Snapshot());
    }

    private string Snapshot() => string.Join(
        '\n',
        Directory.EnumerateFileSystemEntries(_workspace, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(entry => $"{Path.GetRelativePath(_workspace, entry)} {(File.Exists(entry) ? new FileInfo(entry).Length : -1)} {File.GetLastWriteTimeUtc(entry):O}"));

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void Files_the_gitignore_names_are_not_committed_and_tracked_files_that_it_names_stay()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "node_modules", ".bin"));
        File.WriteAllText(Path.Combine(_workspace, "node_modules", ".bin", "jest"), "#!/bin/sh\n");
        File.WriteAllText(Path.Combine(_workspace, "added.txt"), "added\n");

        Assert.True(Run().ExitCode == 0);

        Fetched("%H");
        var files = LocalGit.Git(_source, "ls-tree", "-r", "--name-only", "checkpoint").Split('\n');
        Assert.Contains("added.txt", files);
        Assert.DoesNotContain(files, file => file.StartsWith("node_modules/", StringComparison.Ordinal));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_repository_an_agent_made_inside_the_workspace_is_not_committed_as_a_link_to_a_commit()
    {
        var nested = Path.Combine(_workspace, "vendor", "lib");
        LocalGit.Repository(nested);
        File.WriteAllText(Path.Combine(_workspace, "added.txt"), "added\n");

        var (exit, output) = Run();

        Assert.True(exit == 0, output);
        Fetched("%H");
        Assert.DoesNotContain("160000", LocalGit.Git(_source, "ls-tree", "-r", "checkpoint"));
        Assert.Contains("added.txt", LocalGit.Git(_source, "ls-tree", "-r", "--name-only", "checkpoint"));
    }

    /// <summary>A repository with a second commit that <paramref name="change"/> stages, and a workspace that is a clone of it, for the tests that start from another tip.</summary>
    private (string Source, string Workspace, string Tip, string Bundle) OtherTip(Action<string> change)
    {
        var source = Path.Combine(_root, "other-source");
        LocalGit.Repository(source);
        change(source);
        LocalGit.Git(source, "commit", "--quiet", "-m", "second");
        var bundle = Path.Combine(_root, "other.bundle");
        LocalGit.Bundle(source, bundle, "main");
        var workspace = Path.Combine(_root, "other-workspace");
        LocalGit.Git(_root, "clone", "--quiet", source, workspace);
        LocalGit.Git(workspace, "checkout", "--quiet", "-b", Branch);
        return (source, workspace, LocalGit.Git(source, "rev-parse", "HEAD"), bundle);
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_tracked_file_the_gitignore_names_stays_tracked_and_keeps_the_agents_edit_while_new_files_it_names_are_not_committed()
    {
        var (source, workspace, tip, bundle) = OtherTip(directory =>
        {
            Directory.CreateDirectory(Path.Combine(directory, "node_modules"));
            File.WriteAllText(Path.Combine(directory, "node_modules", "keep.txt"), "tracked\n");
            LocalGit.Git(directory, "add", "--force", "node_modules/keep.txt");
        });
        File.WriteAllText(Path.Combine(workspace, "node_modules", "keep.txt"), "edited\n");
        File.WriteAllText(Path.Combine(workspace, "node_modules", "new.txt"), "new\n");
        File.WriteAllText(Path.Combine(workspace, "added.txt"), "added\n");

        var (exit, output) = Run(bundle, tip, workspace);

        Assert.True(exit == 0, output);
        LocalGit.Git(source, "fetch", "--quiet", _result, $"refs/heads/{Branch}:refs/heads/checkpoint");
        Assert.Equal("edited", LocalGit.Git(source, "show", "checkpoint:node_modules/keep.txt"));
        var files = LocalGit.Git(source, "ls-tree", "-r", "--name-only", "checkpoint").Split('\n');
        Assert.Contains("added.txt", files);
        Assert.DoesNotContain("node_modules/new.txt", files);
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_gitignore_of_everything_does_not_stop_the_commit_and_the_tracked_files_keep_the_agents_edits()
    {
        File.WriteAllText(Path.Combine(_workspace, ".gitignore"), "*\n");
        File.AppendAllText(Path.Combine(_workspace, "README.md"), "edited\n");
        File.WriteAllText(Path.Combine(_workspace, "added.txt"), "added\n");

        var (exit, output) = Run();

        Assert.True(exit == 0, output);
        Assert.Equal(_tip, Fetched("%P"));
        Assert.Equal("hello\nedited", LocalGit.Git(_source, "show", "checkpoint:README.md"));
        Assert.Equal("*", LocalGit.Git(_source, "show", "checkpoint:.gitignore"));
        Assert.DoesNotContain("added.txt", LocalGit.Git(_source, "ls-tree", "-r", "--name-only", "checkpoint"));
        Assert.Contains("app", LocalGit.Git(_source, "show", "checkpoint:src/app.txt"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_file_the_step_user_cannot_read_does_not_stop_the_checkpoint_of_the_rest()
    {
        File.WriteAllText(Path.Combine(_workspace, "secret.txt"), "unreadable\n");
        File.SetUnixFileMode(Path.Combine(_workspace, "secret.txt"), UnixFileMode.None);
        File.WriteAllText(Path.Combine(_workspace, "added.txt"), "added\n");

        var (exit, output) = Run();
        File.SetUnixFileMode(Path.Combine(_workspace, "secret.txt"), UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Assert.True(exit == 0, output);
        Assert.Contains("added.txt", LocalGit.Git(_workspace, "ls-files", "--others", "--exclude-standard"));
        Fetched("%H");
        Assert.Equal("added", LocalGit.Git(_source, "show", "checkpoint:added.txt"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_nested_repository_whose_git_is_a_file_or_that_sits_below_another_directory_is_left_out_too()
    {
        var below = Path.Combine(_workspace, "a", "b", "c");
        LocalGit.Repository(below);
        var worktree = Path.Combine(_workspace, "linked");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: /nowhere\n");
        File.WriteAllText(Path.Combine(worktree, "file.txt"), "x\n");
        File.WriteAllText(Path.Combine(_workspace, "added.txt"), "added\n");

        var (exit, output) = Run();

        Assert.True(exit == 0, output);
        Fetched("%H");
        Assert.DoesNotContain("160000", LocalGit.Git(_source, "ls-tree", "-r", "checkpoint"));
        Assert.Contains("added.txt", LocalGit.Git(_source, "ls-tree", "-r", "--name-only", "checkpoint"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_submodule_the_tip_tracks_is_not_deleted_or_moved_when_the_agent_has_a_repository_at_its_path()
    {
        var (source, workspace, tip, bundle) = OtherTip(directory =>
        {
            LocalGit.Git(directory, "update-index", "--add", "--cacheinfo", $"160000,{new string('a', 40)},vendor/lib");
        });
        var nested = Path.Combine(workspace, "vendor", "lib");
        Directory.CreateDirectory(nested);
        LocalGit.Repository(nested);
        File.WriteAllText(Path.Combine(workspace, "added.txt"), "added\n");

        var (exit, output) = Run(bundle, tip, workspace);

        Assert.True(exit == 0, output);
        LocalGit.Git(source, "fetch", "--quiet", _result, $"refs/heads/{Branch}:refs/heads/checkpoint");
        Assert.Equal($"160000 commit {new string('a', 40)}\tvendor/lib", LocalGit.Git(source, "ls-tree", "checkpoint", "vendor/lib"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_file_whose_name_holds_characters_a_shell_or_a_pathspec_would_read_is_committed_as_it_is()
    {
        File.WriteAllText(Path.Combine(_workspace, "a [b] *.txt"), "odd\n");
        File.WriteAllText(Path.Combine(_workspace, "line\nbreak"), "odd\n");

        Assert.True(Run().ExitCode == 0);

        Fetched("%H");
        var files = LocalGit.Git(_source, "ls-tree", "-r", "--name-only", "-z", "checkpoint").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("a [b] *.txt", files);
        Assert.Contains("line\nbreak", files);
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_bundle_that_does_not_verify_is_refused_before_anything_is_committed()
    {
        AgentWorks();
        File.WriteAllText(Path.Combine(_root, "garbage.bundle"), "not a bundle");

        var (exit, _) = Run(bundle: Path.Combine(_root, "garbage.bundle"));

        Assert.Equal(CheckoutScript.BundleNotVerified, exit);
        Assert.False(File.Exists(_result));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_bundle_of_two_heads_or_of_another_tip_is_refused()
    {
        AgentWorks();
        LocalGit.Git(_source, "branch", "other");
        var two = Path.Combine(_root, "two.bundle");
        LocalGit.Git(_source, "bundle", "create", "--quiet", two, "main", "other");

        Assert.Equal(CheckoutScript.BundleHeads, Run(bundle: two).ExitCode);
        Assert.Equal(CheckoutScript.BundleTip, Run(tip: new string('1', 40)).ExitCode);
        Assert.False(File.Exists(_result));
    }
}
