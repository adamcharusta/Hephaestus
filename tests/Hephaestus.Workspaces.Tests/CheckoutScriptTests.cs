using System.Runtime.Versioning;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.5, AD-6, AR-49: what the checkout container runs, run here with the git and the shell of this machine against a real bundle. Nothing a step left behind survives, the repository is made
/// from an empty template with default configuration, and a bundle that is not what the runtime was told is refused. <see cref="CheckoutEngineTests"/> runs the same script in the tools image.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class CheckoutScriptTests : IDisposable
{
    private const string Branch = "hephaestus/12-fresh-checkout";

    private readonly string _root = Directory.CreateTempSubdirectory("hephaestus-checkout-").FullName;
    private readonly string _workspace;
    private readonly string _bundle;
    private readonly string _tip;

    public CheckoutScriptTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
        var source = Path.Combine(_root, "source");
        _tip = LocalGit.Available ? LocalGit.Repository(source) : new string('0', 40);
        _bundle = Path.Combine(_root, "task.bundle");
        if (LocalGit.Available)
        {
            LocalGit.Bundle(source, _bundle, "main");
        }
    }

    public void Dispose()
    {
        // A directory a step made unreadable cannot be deleted until it is readable again.
        LocalGit.Run(_root, "chmod", ["-R", "u+rwX", _root]);
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private (int ExitCode, string Output) Run(string? tip = null, string? bundle = null) =>
        LocalGit.Run(
            _root,
            "sh",
            ["-c", CheckoutScript.Text, "checkout", _workspace, bundle ?? _bundle],
            new Dictionary<string, string> { ["HEPHAESTUS_BRANCH"] = Branch, ["HEPHAESTUS_TIP"] = tip ?? _tip, ["HOME"] = _root });

    private string Git(params string[] arguments) => LocalGit.Git(_workspace, arguments);

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void The_workspace_is_a_clone_of_the_tip_on_the_task_branch_and_holds_its_files()
    {
        var (exit, output) = Run();

        Assert.True(exit == 0, output);
        Assert.Equal(_tip, Git("rev-parse", "HEAD"));
        Assert.Equal(Branch, Git("symbolic-ref", "--short", "HEAD"));
        Assert.Equal("hello\n", File.ReadAllText(Path.Combine(_workspace, "README.md")));
        Assert.Equal("app\n", File.ReadAllText(Path.Combine(_workspace, "src", "app.txt")));
        Assert.Equal(string.Empty, Git("status", "--porcelain", "--ignored"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_bundle_of_the_kind_the_gateway_makes_is_checked_out_whatever_its_one_head_is_called()
    {
        var source = Path.Combine(_root, "source");
        LocalGit.Git(source, "update-ref", "refs/hephaestus/bundle/018f4b6e-0000-7000-8000-000000000002", _tip);
        var gateway = Path.Combine(_root, "gateway.bundle");
        LocalGit.Bundle(source, gateway, "refs/hephaestus/bundle/018f4b6e-0000-7000-8000-000000000002");

        var (exit, output) = Run(bundle: gateway);

        Assert.True(exit == 0, output);
        Assert.Equal(_tip, Git("rev-parse", "HEAD"));
        Assert.Equal(Branch, Git("symbolic-ref", "--short", "HEAD"));
        Assert.Equal(Branch, Git("branch", "--format=%(refname:short)"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void The_repository_has_default_configuration_no_hook_no_remote_and_no_other_branch()
    {
        Assert.True(Run().ExitCode == 0);

        var configuration = Git("config", "--local", "--list").Split('\n', StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["core.bare=false", "core.filemode=true", "core.logallrefupdates=true", "core.repositoryformatversion=0"], configuration);
        Assert.False(Directory.Exists(Path.Combine(_workspace, ".git", "hooks")));
        Assert.Equal(string.Empty, Git("remote"));
        Assert.Equal(Branch, Git("branch", "--format=%(refname:short)"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_hook_a_configuration_entry_and_an_ignored_runner_planted_by_a_step_are_gone_after_the_next_checkout()
    {
        Assert.True(Run().ExitCode == 0);
        Directory.CreateDirectory(Path.Combine(_workspace, ".git", "hooks"));
        File.WriteAllText(Path.Combine(_workspace, ".git", "hooks", "post-checkout"), "#!/bin/sh\necho planted > /tmp/planted\n");
        Git("config", "core.fsmonitor", "touch /tmp/planted");
        Directory.CreateDirectory(Path.Combine(_workspace, "node_modules", ".bin"));
        File.WriteAllText(Path.Combine(_workspace, "node_modules", ".bin", "jest"), "#!/bin/sh\nexit 0\n");
        File.WriteAllText(Path.Combine(_workspace, "untracked.txt"), "left over\n");

        var (exit, output) = Run();

        Assert.True(exit == 0, output);
        Assert.False(File.Exists(Path.Combine(_workspace, ".git", "hooks", "post-checkout")));
        Assert.DoesNotContain("core.fsmonitor", Git("config", "--local", "--list"));
        Assert.False(Directory.Exists(Path.Combine(_workspace, "node_modules")));
        Assert.False(File.Exists(Path.Combine(_workspace, "untracked.txt")));
        Assert.Equal(string.Empty, Git("status", "--porcelain", "--ignored"));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_step_that_made_a_directory_unreadable_or_a_link_or_a_dot_file_leaves_nothing_either()
    {
        Assert.True(Run().ExitCode == 0);
        Directory.CreateDirectory(Path.Combine(_workspace, "locked", "inner"));
        File.WriteAllText(Path.Combine(_workspace, "locked", "inner", "file"), "x");
        File.CreateSymbolicLink(Path.Combine(_workspace, "link"), "/etc");
        File.WriteAllText(Path.Combine(_workspace, ".hidden"), "x");
        File.SetUnixFileMode(Path.Combine(_workspace, "locked", "inner"), UnixFileMode.None);
        File.SetUnixFileMode(Path.Combine(_workspace, "locked"), UnixFileMode.None);
        File.SetUnixFileMode(_workspace, UnixFileMode.None);

        var (exit, output) = Run();

        Assert.True(exit == 0, output);
        Assert.Equal([".git", ".gitignore", "README.md", "src"], Directory.EnumerateFileSystemEntries(_workspace).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void Everything_the_checkout_made_belongs_to_its_user_and_can_be_written_by_it()
    {
        Assert.True(Run().ExitCode == 0);

        foreach (var entry in Directory.EnumerateFileSystemEntries(_workspace, "*", SearchOption.AllDirectories).Append(_workspace))
        {
            var mode = File.GetUnixFileMode(entry);
            Assert.True(mode.HasFlag(UnixFileMode.UserWrite), $"{entry} is {mode}");
        }
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_tip_other_than_the_one_the_runtime_named_is_refused_and_leaves_no_working_tree()
    {
        var (exit, _) = Run(tip: new string('a', 40));

        Assert.Equal(CheckoutScript.BundleTip, exit);
        Assert.False(File.Exists(Path.Combine(_workspace, "README.md")));
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_bundle_with_two_heads_is_refused()
    {
        var source = Path.Combine(_root, "source");
        LocalGit.Git(source, "branch", "other");
        var two = Path.Combine(_root, "two.bundle");
        LocalGit.Git(source, "bundle", "create", "--quiet", two, "main", "other");

        var (exit, _) = Run(bundle: two);

        Assert.Equal(CheckoutScript.BundleHeads, exit);
    }

    [Fact(Skip = "No git and shell on this machine.", SkipUnless = nameof(LocalGit.Available), SkipType = typeof(LocalGit))]
    public void A_file_that_is_not_a_bundle_is_refused()
    {
        var notABundle = Path.Combine(_root, "not.bundle");
        File.WriteAllText(notABundle, "this is not a bundle");

        var (exit, _) = Run(bundle: notABundle);

        Assert.Equal(CheckoutScript.BundleNotVerified, exit);
    }

    [Fact]
    public void The_script_names_no_branch_and_no_tip_in_its_text_and_holds_no_credential()
    {
        Assert.DoesNotContain("hephaestus/", CheckoutScript.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("token", CheckoutScript.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", CheckoutScript.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--template=", CheckoutScript.Text, StringComparison.Ordinal);
    }
}
