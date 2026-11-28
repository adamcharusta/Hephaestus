using System.Globalization;
using System.Text;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.PullRequests;

/// <summary>
/// The description of a pull request, in the sections and the order <c>EXPERIENCE.md</c> fixes (UX-DR-19): What changed, Task, How it was checked, and Who did what; a section with nothing to say is left out. Core composes it from the facts the
/// records hold (AD-19); the fixed words are resources in the Task's language (AD-21), and whatever an Agent or a person wrote is neutralised first, so that it cannot forge a marker, mention anybody, close an issue, or give a command (FR-4 is
/// the gateway's, which masks stored secret values last). The Task's issue is named by its number alone, with no closing keyword, so that merging the pull request does not close it: that the Task is merged is Hephaestus's to observe, and the issue is the Owner's.
/// </summary>
public static class PullRequestDescription
{
    /// <summary>How long a title may be; GitHub refuses a longer one.</summary>
    public const int MaximumTitleLength = 256;

    private const int MaximumCommandLength = 200;

    /// <summary>The title: the issue's, on one line, neutralised, and within what GitHub accepts.</summary>
    public static string Title(PullRequestFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var title = string.Join(' ', AgentText.Neutralise(facts.Title).Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();
        return title.Length > MaximumTitleLength ? title[..MaximumTitleLength].TrimEnd() : title;
    }

    /// <summary>The description of the pull request of the Task whose issue is <paramref name="issueNumber"/>.</summary>
    public static string Compose(PullRequestFacts facts, int issueNumber, Language language)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var catalog = MessageCatalog.Instance;
        var text = new StringBuilder();
        var summary = AgentText.Neutralise(facts.Summary).Trim();
        if (summary.Length > 0)
        {
            Section(text, catalog.Resolve(MessageIds.PullRequestSectionWhatChanged, language)).Append(summary).Append('\n');
        }

        Section(text, catalog.Resolve(MessageIds.PullRequestSectionTask, language))
            .Append(catalog.Resolve(new Message(MessageIds.PullRequestTaskIssue, "#" + issueNumber.ToString(CultureInfo.InvariantCulture)), language))
            .Append('\n');

        if (facts.Checks.Count > 0)
        {
            Section(text, catalog.Resolve(MessageIds.PullRequestSectionChecked, language))
                .Append("| ").Append(catalog.Resolve(MessageIds.PullRequestColumnCommand, language))
                .Append(" | ").Append(catalog.Resolve(MessageIds.PullRequestColumnResult, language))
                .Append(" | ").Append(catalog.Resolve(MessageIds.PullRequestColumnDuration, language)).Append(" |\n| --- | --- | --- |\n");
            foreach (var check in facts.Checks)
            {
                text.Append("| ").Append(Code(check.Command, MaximumCommandLength, escapePipe: true))
                    .Append(" | ").Append(Result(check, language))
                    .Append(" | ").Append(Duration(check.DurationMilliseconds))
                    .Append(" |\n");
            }
        }

        var who = facts.Implementer;
        Section(text, catalog.Resolve(MessageIds.PullRequestSectionWho, language))
            .Append(catalog.Resolve(new Message(MessageIds.PullRequestImplementer, Code(who.Agent), Code(who.Model), Code(who.Effort)), language))
            .Append('\n');
        return text.ToString();
    }

    private static StringBuilder Section(StringBuilder text, string title) => (text.Length > 0 ? text.Append('\n') : text).Append("## ").Append(title).Append("\n\n");

    /// <summary>The status tag, which is never translated, and the exit status the runtime saw of a command that did not pass.</summary>
    private static string Result(PullRequestCheck check, Language language) =>
        check.ExitCode is { } code && check.Status != "passed"
            ? $"{Cell(check.Status)}, {MessageCatalog.Instance.Resolve(new Message(MessageIds.PullRequestResultExit, code.ToString(CultureInfo.InvariantCulture)), language)}"
            : Cell(check.Status);

    private static string Duration(long? milliseconds) =>
        milliseconds is { } value ? (value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s" : "-";

    /// <summary>A value as a code span on one line: neutralised, with no backtick to end the span and, in a table, no pipe to end the cell.</summary>
    private static string Code(string value, int maximumLength = 100, bool escapePipe = false)
    {
        var line = string.Join(' ', AgentText.Neutralise(value).Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Replace('`', '\'');
        if (line.Length > maximumLength)
        {
            line = line[..maximumLength] + "...";
        }

        return "`" + (escapePipe ? line.Replace("|", "\\|", StringComparison.Ordinal) : line) + "`";
    }

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
