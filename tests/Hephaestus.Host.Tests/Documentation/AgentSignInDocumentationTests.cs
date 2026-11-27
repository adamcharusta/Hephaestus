using Hephaestus.Core.Readiness;
using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Documentation;

/// <summary>
/// Story 3.10, FR-25, AR-58, AD-20: the installation documentation presents both modes on an equal footing with the terms of each and the risk of a subscription, names the conservative choice,
/// says what Claude Code's subscription credential is and why Codex CLI is API key only, fixes the probe image, and every claim the threat model makes about the credential names a test that exists.
/// </summary>
public class AgentSignInDocumentationTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, relative));

    [Fact]
    public void The_guide_presents_both_modes_side_by_side_with_the_terms_that_apply_to_each()
    {
        var guide = Read("docs/agent-sign-in.md");

        Assert.Contains("| | API key | Subscription |", guide, StringComparison.Ordinal);
        Assert.Contains("Commercial Terms of Service", guide, StringComparison.Ordinal);
        Assert.Contains("Consumer Terms of Service", guide, StringComparison.Ordinal);
        Assert.Contains("assume ordinary, individual usage", guide, StringComparison.Ordinal);
        Assert.Contains("without prior notice", guide, StringComparison.Ordinal);
        Assert.Contains("collect, store, or intermediate", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void The_guide_names_an_API_key_with_a_spend_cap_as_the_conservative_choice()
    {
        var guide = Read("docs/agent-sign-in.md");

        Assert.Contains("The conservative choice is an API key with a spend cap at the vendor", guide, StringComparison.Ordinal);
        Assert.Contains("API key with a spend cap at the vendor is the conservative choice", Read("README.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_guide_says_what_the_subscription_credential_of_Claude_Code_is_and_why_Codex_CLI_has_an_API_key_only()
    {
        var guide = Read("docs/agent-sign-in.md");

        Assert.Contains("claude setup-token", guide, StringComparison.Ordinal);
        Assert.Contains("Codex CLI is supported with an API key only", guide, StringComparison.Ordinal);
        Assert.Contains("must be kept and refreshed between runs", guide, StringComparison.Ordinal);
        Assert.Contains("cannot be listed from the CLI", guide, StringComparison.Ordinal);
        Assert.Contains("Codex CLI contacts `chatgpt.com` even with an API key", guide, StringComparison.Ordinal);
        Assert.Contains("Hephaestus implements no sign-in", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void The_probe_image_is_named_by_its_digest_in_the_guide_and_in_the_Agent_CLI_document()
    {
        Assert.Contains($"`{ProbeImage.Reference}`", Read("docs/agent-sign-in.md"), StringComparison.Ordinal);
        Assert.Contains($"`{ProbeImage.Reference}`", Read("docs/agent-clis.md"), StringComparison.Ordinal);
        Assert.Contains("No Task volume, no context, no Methodology", Read("docs/agent-sign-in.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_README_documents_the_three_commands_and_links_the_guide()
    {
        var readme = Read("README.md");

        Assert.Contains("hephaestus agent set-auth claude-code --mode api-key", readme, StringComparison.Ordinal);
        Assert.Contains("hephaestus agent check claude-code", readme, StringComparison.Ordinal);
        Assert.Contains("hephaestus agent list", readme, StringComparison.Ordinal);
        Assert.Contains("docs/agent-sign-in.md", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decision_record_is_accepted_and_listed()
    {
        var record = Read("docs/adr/0026-agent-sign-in-and-the-probe.md");

        Assert.Contains("Accepted", record, StringComparison.Ordinal);
        Assert.Contains("RunProbeAsync", record, StringComparison.Ordinal);
        Assert.Contains("AgentCapacities", record, StringComparison.Ordinal);
        Assert.Contains("0026-agent-sign-in-and-the-probe.md", Read("docs/adr/README.md"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("StepSecretsTests")]
    [InlineData("ProbeContainerTests")]
    [InlineData("ProbeEngineTests")]
    [InlineData("AgentSignInServiceTests")]
    [InlineData("AgentEndpointsTests")]
    [InlineData("AgentSetupTests")]
    [InlineData("AgentCommandTests")]
    public void The_threat_model_names_a_test_of_the_credential_that_exists(string test)
    {
        Assert.Contains(test, Read("docs/security/threat-model.md"), StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(RepositoryRoot.Path, "tests"), $"{test}.cs", SearchOption.AllDirectories));
    }
}
