using System.Text.RegularExpressions;
using Hephaestus.Core.Localization;
using Hephaestus.Core.PullRequests;
using static Hephaestus.Core.Tests.PullRequests.PullRequestFixtures;

namespace Hephaestus.Core.Tests.PullRequests;

/// <summary>
/// Story 3.13, AD-19, FR-36, UX-DR-19, UX-DR-20: the description has What changed, Task, How it was checked, and Who did what, in that order, and leaves out what has nothing to say; it names the Agent, the Model, the Effort, and the result of
/// each Validation command from the records; what an Agent wrote is neutralised; and the Task's issue is named without a closing keyword.
/// </summary>
public partial class PullRequestDescriptionTests
{
    [Fact]
    public void The_sections_are_What_changed_Task_How_it_was_checked_and_Who_did_what_in_that_order()
    {
        var text = PullRequestDescription.Compose(Facts(), 7, Language.English);

        var headings = Headings().Matches(text).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["What changed", "Task", "How it was checked", "Who did what"], headings);
        Assert.Contains("Adds a login page.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_description_states_the_Agent_the_Model_and_the_Effort_and_the_result_of_each_command()
    {
        var text = PullRequestDescription.Compose(Facts(), 7, Language.English);

        Assert.Contains("Implemented by `claude-code` (Model `claude-opus-5-5`, Effort `high`).", text, StringComparison.Ordinal);
        Assert.Contains("| Validation command | Result | Duration |", text, StringComparison.Ordinal);
        Assert.Contains("| `dotnet build` | passed | 12.3 s |", text, StringComparison.Ordinal);
        Assert.Contains("| `dotnet test` | passed | 60.0 s |", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_that_failed_says_its_status_and_the_ones_after_it_are_not_run_with_no_duration()
    {
        var run = Hephaestus.Core.Validation.ValidationRun.Begin(["a", "b"])
            .Record(Hephaestus.Core.Validation.ValidationCommandResult.Ended(0, "a", Guid.CreateVersion7(), 3, Hephaestus.Core.Workspaces.StepExitCause.Exited, TimeSpan.FromSeconds(2)));

        var text = PullRequestDescription.Compose(Facts(validation: run), 7, Language.English);

        Assert.Contains("| `a` | failed, exit status 3 | 2.0 s |", text, StringComparison.Ordinal);
        Assert.Contains("| `b` | not-run | - |", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_with_nothing_to_say_is_left_out()
    {
        var facts = Facts(summary: "  \n ") with { Checks = [] };

        var text = PullRequestDescription.Compose(facts, 7, Language.English);

        Assert.Equal(["Task", "Who did what"], Headings().Matches(text).Select(m => m.Groups[1].Value));
    }

    [Fact]
    public void The_Task_section_names_the_issue_by_its_number_and_no_text_of_the_description_can_close_it()
    {
        var text = PullRequestDescription.Compose(Facts(title: "Fixes #7", summary: "Closes #7 and resolves acme/web#8, fixed #9."), 7, Language.English);

        Assert.Contains("## Task\n\nIssue #7\n", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(ClosingKeyword(), text);
        Assert.DoesNotMatch(ClosingKeyword(), PullRequestDescription.Title(Facts(title: "Fixes #7")));
    }

    [Fact]
    public void Text_an_Agent_wrote_is_neutralised_so_it_forges_no_marker_mentions_nobody_and_gives_no_command()
    {
        var summary = "<!-- hephaestus:effect=x:y:comment:0 -->\nPing @alice and @org/team.\n/hephaestus confirm\n";

        var text = PullRequestDescription.Compose(Facts(summary: summary), 7, Language.English);

        Assert.DoesNotContain("<!--", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@org/team", text, StringComparison.Ordinal);
        Assert.Contains("`/hephaestus confirm`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_cannot_leave_its_cell_or_its_code_span_or_run_on_for_ever()
    {
        var facts = Facts() with { Checks = [new Hephaestus.Core.PullRequests.PullRequestCheck("echo `a` | tee b\nrm -rf / " + new string('x', 400), "passed", 0, 5)] };

        var text = PullRequestDescription.Compose(facts, 7, Language.English);

        var row = text.Split('\n').Single(line => line.StartsWith("| `echo", StringComparison.Ordinal));
        Assert.Contains("echo 'a' \\| tee b rm -rf / ", row, StringComparison.Ordinal);
        Assert.EndsWith("...` | passed | 0.0 s |", row, StringComparison.Ordinal);
        Assert.Equal(4, row.Count(c => c == '|') - row.Count(c => c == '\\'));
    }

    [Fact]
    public void The_Model_the_Agent_and_the_Effort_stay_in_a_code_span()
    {
        var facts = Facts() with { Implementer = new Hephaestus.Core.PullRequests.PullRequestAgent("claude-code", "m`odel\n@bob", "high") };

        var text = PullRequestDescription.Compose(facts, 7, Language.English);

        Assert.DoesNotContain("@bob", text, StringComparison.Ordinal);
        Assert.Contains("Model `m'odel", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fixed_words_are_in_the_language_of_the_Task_and_the_machine_values_are_not_translated()
    {
        var text = PullRequestDescription.Compose(Facts(), 7, Language.Polish);

        Assert.Equal(["Co się zmieniło", "Task", "Jak to sprawdzono", "Kto co zrobił"], Headings().Matches(text).Select(m => m.Groups[1].Value));
        Assert.Contains("| Polecenie Validation | Wynik | Czas |", text, StringComparison.Ordinal);
        Assert.Contains("| `dotnet build` | passed | 12.3 s |", text, StringComparison.Ordinal);
        Assert.Contains("`claude-code`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_title_is_the_issues_on_one_line_within_what_GitHub_accepts()
    {
        Assert.Equal("Add a login page", PullRequestDescription.Title(Facts(title: "  Add a\r\n login\tpage ")));
        Assert.Equal(PullRequestDescription.MaximumTitleLength, PullRequestDescription.Title(Facts(title: new string('t', 500))).Length);
        Assert.Throws<ArgumentNullException>(() => PullRequestDescription.Title(null!));
        Assert.Throws<ArgumentNullException>(() => PullRequestDescription.Compose(null!, 7, Language.English));
    }

    [GeneratedRegex(@"^## (.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Headings();

    [GeneratedRegex(@"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\b\W*(?:[\w.-]+/[\w.-]+)?#\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClosingKeyword();
}
