using System.Globalization;
using System.CommandLine;
using Hephaestus.Core.Localization;
using Hephaestus.Host.Api;

namespace Hephaestus.Host.Cli;

/// <summary>The commands of the Projects (Story 1.10): <c>project add</c>, <c>project list</c>, and <c>project remove</c>.</summary>
internal sealed partial class CliApplication
{
    /// <summary>Runs the command when it is one of the Project commands; null when it is not.</summary>
    private async Task<int?> RunProjectAsync(Command command, ParseResult parse, CommandTree tree, bool json, CancellationToken cancellationToken)
    {
        if (command == tree.ProjectAdd)
        {
            var body = System.Text.Json.JsonSerializer.Serialize(new ProjectRequest(parse.GetValue(tree.ProjectBaseBranch)), ApiJson.Default.ProjectRequest);
            return await ShowAsync(HttpMethod.Put, ProjectPath(parse.GetValue(tree.ProjectAddName)!), body, json, ApiJson.Default.ProjectDocument, IsWhole, (project, _) => WriteProjectRegistered(project), cancellationToken);
        }

        if (command == tree.ProjectList)
        {
            var path = parse.GetValue(tree.ProjectAll) ? "/api/projects?all=true" : "/api/projects";
            return await ShowAsync(HttpMethod.Get, path, null, json, ApiJson.Default.ProjectListDocument, list => list.Projects is not null && list.Projects.All(IsWhole), (list, _) => WriteProjects(list.Projects, parse.GetValue(tree.ProjectAll)), cancellationToken);
        }

        if (command == tree.ProjectRemove)
        {
            return await ShowAsync(HttpMethod.Delete, ProjectPath(parse.GetValue(tree.ProjectRemoveName)!), null, json, ApiJson.Default.ProjectDocument, IsWhole, (project, _) => WriteSentence(MessageIds.CliProjectRemoved, project.Name, project.RepositoryId), cancellationToken);
        }

        return null;
    }

    /// <summary>The name goes in the query as typed, escaped; a name that is not <c>owner/repo</c> is refused by the API, not resolved into another address.</summary>
    private static string ProjectPath(string name) => "/api/projects?name=" + Uri.EscapeDataString(name);

    private static bool IsWhole(ProjectDocument? project) => project is { Name: not null, BaseBranch: not null, RegisteredAt: not null };

    private void WriteProjectRegistered(ProjectDocument project)
    {
        var sentence = project.Change switch
        {
            "registered" => MessageIds.CliProjectAdded,
            "updated" => MessageIds.CliProjectUpdated,
            _ => MessageIds.CliProjectAlready,
        };
        WriteSentence(sentence, project.Name, project.RepositoryId, project.BaseBranch);
        if (project.BaseBranchProtected is { } isProtected)
        {
            WriteSentence(isProtected ? MessageIds.CliProjectBranchProtected : MessageIds.CliProjectBranchUnprotected, project.BaseBranch);
        }
    }

    private void WriteProjects(IReadOnlyList<ProjectDocument> projects, bool all)
    {
        if (projects.Count == 0)
        {
            output.WriteLine(Caption(MessageIds.CliProjectListEmpty));
            return;
        }

        // A removed Project is listed only when the Owner asks, so the column of removal times is there exactly then.
        var withRemoved = all;
        var header = new List<string> { Caption(MessageIds.CliProjectColumnName), Caption(MessageIds.CliProjectColumnId), Caption(MessageIds.CliProjectColumnBaseBranch), Caption(MessageIds.CliProjectColumnRegistered) };
        if (withRemoved)
        {
            header.Add(Caption(MessageIds.CliProjectColumnRemoved));
        }

        var rows = new List<string[]> { header.ToArray() };
        rows.AddRange(projects.Select(p =>
        {
            var row = new List<string> { p.Name, p.RepositoryId.ToString(CultureInfo.InvariantCulture), p.BaseBranch, p.RegisteredAt };
            if (withRemoved)
            {
                row.Add(p.RemovedAt ?? Absent);
            }

            return row.ToArray();
        }));
        TableWriter.Write(output, rows);
    }
}
