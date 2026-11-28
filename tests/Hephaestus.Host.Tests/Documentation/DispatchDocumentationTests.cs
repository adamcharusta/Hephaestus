using Hephaestus.Core.Runs;
using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Documentation;

/// <summary>Story 3.14, AD-1, AD-17, AD-18: what the Owner reads says what starts work, which Task starts, what is kept when it does, and what each step is; the decision record names what it decides, and the documents use the names the product uses.</summary>
public class DispatchDocumentationTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, relative));

    [Fact]
    public void A_decision_record_names_the_window_the_dispatch_rule_the_snapshot_the_step_runs_and_the_lazy_runtime()
    {
        var record = Read("docs/adr/0030-start-a-project-on-demand-and-drive-a-task-from-its-records.md");

        Assert.Contains("Accepted", record, StringComparison.Ordinal);
        foreach (var name in new[] { "start-requested", "window-already-open", "DispatchPolicy", "DispatchAsync", "TaskSnapshot", "StepRun", "concluded", "LazyWorkspaceRuntime", "drained", "stopped" })
        {
            Assert.Contains(name, record, StringComparison.Ordinal);
        }

        Assert.Contains("0030-start-a-project-on-demand-and-drive-a-task-from-its-records.md", Read("docs/adr/README.md"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("project start")]
    [InlineData("project stop")]
    [InlineData("signal show")]
    [InlineData("--no-wait")]
    [InlineData("window-already-open")]
    [InlineData("window-not-open")]
    [InlineData("project-not-registered")]
    public void The_document_names_every_command_and_note_the_Owner_meets(string name)
    {
        Assert.Contains(name, Read("docs/dispatch.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_status_of_a_step_run_is_named_in_the_document_by_the_name_the_store_keeps()
    {
        var document = Read("docs/dispatch.md");

        foreach (var status in Enum.GetValues<StepRunStatus>())
        {
            Assert.Contains($"`{status.Tag()}`", document, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_README_says_how_to_start_work_and_what_a_Task_does_and_links_the_document_and_the_record()
    {
        var readme = Read("README.md");

        Assert.Contains("### Starting work on a Project", readme, StringComparison.Ordinal);
        Assert.Contains("hephaestus project start acme/shop", readme, StringComparison.Ordinal);
        Assert.Contains("at most one Task runs in the whole installation", readme, StringComparison.Ordinal);
        Assert.Contains("docs/dispatch.md", readme, StringComparison.Ordinal);
        Assert.Contains("ADR-0030", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing starts a Task yet", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing starts a step yet", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_documents_that_waited_for_the_dispatch_no_longer_say_that_they_wait()
    {
        foreach (var file in new[] { "docs/step-containers.md", "docs/pull-requests.md", "docs/methodologies.md", "docs/agent-sign-in.md" })
        {
            Assert.DoesNotContain("(Story 3.14)", Read(file), StringComparison.Ordinal);
            Assert.DoesNotContain("in Story 3.14", Read(file), StringComparison.Ordinal);
        }
    }
}
