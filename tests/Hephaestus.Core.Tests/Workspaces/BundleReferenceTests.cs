using Hephaestus.Core.GitHub;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

/// <summary>Story 3.5, AD-9: a bundle is named by a path under the bundles directory, its checksum, and the one commit it carries, and a checkout is asked for a Task branch and nothing else.</summary>
public class BundleReferenceTests
{
    private static readonly string Checksum = new('a', 64);
    private static readonly string Tip = new('b', 40);

    [Theory]
    [InlineData("task/step-checkout.bundle")]
    [InlineData("a.bundle")]
    public void A_relative_path_of_a_bundle_file_is_accepted(string path)
    {
        var bundle = new BundleReference(path, Checksum, Tip);

        Assert.Equal(path, bundle.Path);
        Assert.Equal(Checksum, bundle.Sha256);
        Assert.Equal(Tip, bundle.Tip);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/etc/passwd.bundle")]
    [InlineData("../outside.bundle")]
    [InlineData("a/../../outside.bundle")]
    [InlineData("task/step-checkout.tar")]
    [InlineData("task/step-checkout")]
    [InlineData("a\\b.bundle")]
    public void A_path_that_could_leave_the_directory_or_is_not_a_bundle_file_is_refused(string path)
    {
        Assert.Throws<ArgumentException>(() => new BundleReference(path, Checksum, Tip));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEF")]
    [InlineData("not hex")]
    public void A_checksum_that_is_not_a_sha256_in_lower_case_is_refused(string checksum)
    {
        Assert.Throws<ArgumentException>(() => new BundleReference("a.bundle", checksum, Tip));
        Assert.Throws<ArgumentException>(() => new BundleReference("a.bundle", new string('A', 64), Tip));
        Assert.Throws<ArgumentException>(() => new BundleReference("a.bundle", new string('a', 63), Tip));
    }

    [Theory]
    [InlineData("main")]
    [InlineData("")]
    [InlineData("--upload-pack=x")]
    public void A_tip_that_is_not_a_commit_id_is_refused(string tip)
    {
        Assert.Throws<ArgumentException>(() => new BundleReference("a.bundle", Checksum, tip));
    }

    [Fact]
    public void A_commit_id_has_forty_lower_case_hexadecimal_digits_because_the_repositories_are_sha_1()
    {
        Assert.True(BundleReference.IsCommitId(new string('0', 40)));
        Assert.False(BundleReference.IsCommitId(new string('f', 64)));
        Assert.False(BundleReference.IsCommitId(new string('0', 41)));
        Assert.False(BundleReference.IsCommitId(new string('F', 40)));
        Assert.False(BundleReference.IsCommitId(null));
        Assert.False(BundleReference.IsCommitId("HEAD"));
    }

    [Theory]
    [InlineData("hephaestus/12")]
    [InlineData("hephaestus/12-fresh-checkout")]
    [InlineData("hephaestus/7-a1-b2")]
    [InlineData("hephaestus/2147483647-the-largest-issue-number")]
    public void The_branch_of_a_checkout_is_a_task_branch(string branch)
    {
        var request = new CheckoutRequest(Guid.NewGuid(), Guid.NewGuid(), branch, new BundleReference("a.bundle", Checksum, Tip));

        Assert.Equal(branch, request.Branch);
        Assert.True(BranchName.IsTaskBranch(branch));
    }

    [Theory]
    [InlineData("main")]
    [InlineData("")]
    [InlineData("hephaestus/")]
    [InlineData("hephaestus/12-")]
    [InlineData("hephaestus/12--double")]
    [InlineData("hephaestus/12-UPPER")]
    [InlineData("hephaestus/12/other")]
    [InlineData("hephaestus/12-a;rm -rf")]
    [InlineData("hephaestus/12-a\nb")]
    [InlineData("-hephaestus/12")]
    [InlineData("hephaestus/12-..")]
    public void A_name_that_is_not_a_task_branch_is_refused(string branch)
    {
        Assert.False(BranchName.IsTaskBranch(branch));
        Assert.Throws<ArgumentException>(() => new CheckoutRequest(Guid.NewGuid(), Guid.NewGuid(), branch, new BundleReference("a.bundle", Checksum, Tip)));
    }

    [Fact]
    public void A_name_made_from_any_title_is_a_task_branch()
    {
        foreach (var title in new[] { "Fix the login", "Zażółć gęślą jaźń", "   ", "!!!", new string('x', 200), "a-b--c" })
        {
            Assert.True(BranchName.IsTaskBranch(BranchName.For(123456789, title)), title);
        }

        Assert.False(BranchName.IsTaskBranch(null));
    }

    [Fact]
    public void A_name_made_for_the_largest_issue_number_is_a_task_branch()
    {
        Assert.True(BranchName.IsTaskBranch(BranchName.For(int.MaxValue, "A title")));
    }

    [Fact]
    public void A_bundle_request_is_made_of_a_commit_id_and_nothing_else()
    {
        var request = new BundleRequest(42, Tip, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(42, request.RepositoryId);
        Assert.Equal(Tip, request.Commit);
        Assert.Throws<ArgumentException>(() => new BundleRequest(42, "main", Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new BundleRequest(42, new string('B', 40), Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void A_checkout_needs_a_bundle()
    {
        Assert.Throws<ArgumentNullException>(() => new CheckoutRequest(Guid.NewGuid(), Guid.NewGuid(), "hephaestus/1", null!));
    }

    [Fact]
    public void The_results_of_a_mirror_say_what_was_made_or_why_not()
    {
        var bundle = new BundleReference("a.bundle", Checksum, Tip);

        Assert.Equal(MirrorFailure.None, MirrorResult.Fetched.Failure);
        Assert.Equal(MirrorFailure.GitFailed, MirrorResult.Failed(MirrorFailure.GitFailed, "no git").Failure);
        Assert.Equal("no git", MirrorResult.Failed(MirrorFailure.GitFailed, "no git").Detail);
        Assert.Same(bundle, BundleResult.Made(bundle).Bundle);
        Assert.Equal(MirrorFailure.None, BundleResult.Made(bundle).Failure);
        Assert.Null(BundleResult.Failed(MirrorFailure.CommitMissing).Bundle);
        Assert.Equal(MirrorFailure.CommitMissing, BundleResult.Failed(MirrorFailure.CommitMissing).Failure);
    }

    [Theory]
    [InlineData(MessageIds.WorkspaceToolsImageUnavailable)]
    [InlineData(MessageIds.WorkspaceToolsImageMissing)]
    [InlineData(MessageIds.WorkspaceTaskVolumeMissing)]
    [InlineData(MessageIds.WorkspaceCheckoutBundleLocation)]
    [InlineData(MessageIds.WorkspaceCheckoutBundleFile)]
    [InlineData(MessageIds.WorkspaceCheckoutBundleSize)]
    [InlineData(MessageIds.WorkspaceCheckoutBundleChecksum)]
    [InlineData(MessageIds.WorkspaceCheckoutBundleNotVerified)]
    [InlineData(MessageIds.WorkspaceCheckoutBundleHeads)]
    [InlineData(MessageIds.WorkspaceCheckoutBundleTip)]
    [InlineData(MessageIds.WorkspaceCheckoutFailed)]
    [InlineData(MessageIds.WorkspaceCheckoutTimedOut)]
    public void Every_message_of_the_checkout_is_written_in_both_languages(string id)
    {
        var english = MessageCatalog.Instance.Resolve(new Message(id, "1", "2"), Language.English);
        var polish = MessageCatalog.Instance.Resolve(new Message(id, "1", "2"), Language.Polish);

        Assert.NotEqual(english, polish);
        Assert.Contains("1", english, StringComparison.Ordinal);
        Assert.Contains("1", polish, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bundle_that_is_not_whole_names_the_one_commit_it_needs()
    {
        var prerequisite = new string('c', 40);

        var bundle = new BundleReference("task/step-checkpoint.bundle", Checksum, Tip, prerequisite);

        Assert.Equal(prerequisite, bundle.Prerequisite);
        Assert.Null(new BundleReference("a.bundle", Checksum, Tip).Prerequisite);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC")]
    public void A_prerequisite_that_is_not_a_commit_id_is_refused(string prerequisite)
    {
        Assert.Throws<ArgumentException>(() => new BundleReference("a.bundle", Checksum, Tip, prerequisite));
    }
}
