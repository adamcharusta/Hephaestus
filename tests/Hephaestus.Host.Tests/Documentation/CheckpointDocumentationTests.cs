using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Documentation;

/// <summary>Story 3.6, AD-9: a decision record names the checkpoint's own git directory, its one result file, and its refusals; the documents the Owner reads say what a checkpoint keeps and what it does not.</summary>
public class CheckpointDocumentationTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, relative));

    [Fact]
    public void A_decision_record_names_the_git_directory_the_one_commit_the_one_file_and_the_refusals()
    {
        var record = Read("docs/adr/0023-checkpoint-as-one-commit.md");

        Assert.Contains("Accepted", record, StringComparison.Ordinal);
        Assert.Contains("GIT_DIR", record, StringComparison.Ordinal);
        Assert.Contains("GIT_WORK_TREE", record, StringComparison.Ordinal);
        Assert.Contains("git commit-tree", record, StringComparison.Ordinal);
        Assert.Contains("CheckpointCommit", record, StringComparison.Ordinal);
        Assert.Contains("/checkpoint/result.bundle", record, StringComparison.Ordinal);
        Assert.Contains("infrastructure error", record, StringComparison.Ordinal);
        Assert.Contains("0023-checkpoint-as-one-commit.md", Read("docs/adr/README.md"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docs/step-containers.md")]
    [InlineData("docs/adr/0023-checkpoint-as-one-commit.md")]
    public void The_documents_name_the_five_trailers_the_parent_and_what_is_not_kept(string document)
    {
        var text = Read(document);

        foreach (var trailer in new[] { "Hephaestus-Task", "Hephaestus-Step", "Hephaestus-Agent", "Hephaestus-Model", "Hephaestus-Effort" })
        {
            Assert.Contains(trailer, text, StringComparison.Ordinal);
        }

        Assert.Contains("last pushed tip", text, StringComparison.Ordinal);
        Assert.Contains("ignored", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_threat_model_names_the_tests_that_show_the_checkpoint_claims()
    {
        var model = Read("docs/security/threat-model.md");

        Assert.Contains("CheckpointScriptTests", model, StringComparison.Ordinal);
        Assert.Contains("CheckpointEngineTests", model, StringComparison.Ordinal);
        Assert.Contains("CheckpointContainerTests", model, StringComparison.Ordinal);
    }
}
