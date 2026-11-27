using Hephaestus.Core.Context;
using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Documentation;

/// <summary>Story 3.11, AD-12, AD-14: the guide to Methodologies names every context file and says what a step run records, and a decision record is indexed.</summary>
public class MethodologyDocumentationTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, relative));

    [Fact]
    public void The_guide_names_every_context_file()
    {
        var guide = Read("docs/methodologies.md");

        Assert.All(ContextFiles.All, name => Assert.Contains($"`{name}`", guide, StringComparison.Ordinal));
    }

    [Fact]
    public void The_guide_says_what_a_step_run_records_and_that_a_template_has_no_substitution()
    {
        var guide = Read("docs/methodologies.md");

        Assert.Contains("SHA-256", guide, StringComparison.Ordinal);
        Assert.Contains("no placeholders", guide, StringComparison.Ordinal);
        Assert.Contains("docs/methodologies.md", Read("docs/step-containers.md").Replace("](methodologies.md)", "docs/methodologies.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_decision_record_is_accepted_and_indexed()
    {
        var record = Read("docs/adr/0027-one-instruction-per-step-kind.md");

        Assert.Contains("Accepted", record, StringComparison.Ordinal);
        Assert.Contains("StepContextAssembler", record, StringComparison.Ordinal);
        Assert.Contains("ResultSchemas.Violations", record, StringComparison.Ordinal);
        Assert.Contains("0027-one-instruction-per-step-kind.md", Read("docs/adr/README.md"), StringComparison.Ordinal);
    }
}
