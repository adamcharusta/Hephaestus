using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Documentation;

/// <summary>Story 3.7, AD-9, AD-19: a decision record names the checks, their order, and the refusals; the documents the Owner reads say what is pushed and what blocks it; every claim the threat model makes about the push names a test that exists.</summary>
public class PushDocumentationTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, relative));

    [Fact]
    public void A_decision_record_names_the_port_the_quarantine_the_checks_and_the_case_policy()
    {
        var record = Read("docs/adr/0024-push-through-the-gateways-checks.md");

        Assert.Contains("Accepted", record, StringComparison.Ordinal);
        Assert.Contains("ITaskBranches", record, StringComparison.Ordinal);
        Assert.Contains("refs/hephaestus/quarantine", record, StringComparison.Ordinal);
        Assert.Contains("fsckObjects", record, StringComparison.Ordinal);
        Assert.Contains("--force", record, StringComparison.Ordinal);
        Assert.Contains("case-insensitive", record, StringComparison.Ordinal);
        Assert.Contains("PushBlocked", record, StringComparison.Ordinal);
        Assert.Contains("confirm", record, StringComparison.Ordinal);
        Assert.Contains("0024-push-through-the-gateways-checks.md", Read("docs/adr/README.md"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("import")]
    [InlineData("branch")]
    [InlineData("remote-moved")]
    [InlineData("fast-forward")]
    [InlineData("secret")]
    [InlineData("protected-path")]
    public void The_record_names_every_check_by_the_tag_an_Escalation_carries(string check)
    {
        Assert.Contains($"`{check}`", Read("docs/adr/0024-push-through-the-gateways-checks.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_container_document_and_the_README_say_that_only_the_gateway_pushes_and_what_blocks_it()
    {
        var containers = Read("docs/step-containers.md");
        var readme = Read("README.md");

        Assert.Contains("only code that pushes", containers, StringComparison.Ordinal);
        Assert.Contains("no plus, no force, and no delete", containers, StringComparison.Ordinal);
        Assert.Contains("`confirm`", containers, StringComparison.Ordinal);
        Assert.Contains("Only the gateway pushes that bundle", readme, StringComparison.Ordinal);
        Assert.Contains("ADR-0024", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void The_threat_model_names_the_tests_that_show_the_push_claims()
    {
        var model = Read("docs/security/threat-model.md");

        foreach (var test in new[] { "GitHubPushTests", "SecretScanTests", "OrchestratorLoopPushTests", "PushRequestTests", "GitPushRulesTests", "ProtectedPathMatcherTests" })
        {
            Assert.Contains(test, model, StringComparison.Ordinal);
        }
    }
}
