using System.Globalization;
using System.CommandLine;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Projects;
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

        if (command == tree.ProjectCheck)
        {
            return await CheckProjectAsync(parse.GetValue(tree.ProjectCheckName)!, json, cancellationToken);
        }

        return await RunSignalCommandAsync(command, parse, tree, json, cancellationToken);
    }

    /// <summary>
    /// Reads <c>hephaestus.yml</c> from the Project's Base Branch and says whether it is valid. Exit code 0 means a valid file: a file that is missing or invalid
    /// is the answer to the question and is written to standard output, and it ends the command with 1, so that a script can rely on the check.
    /// </summary>
    private async Task<int> CheckProjectAsync(string name, bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync(HttpMethod.Get, "/api/projects/check?name=" + Uri.EscapeDataString(name), body: null, cancellationToken);
        if (answer is null)
        {
            return Failure;
        }

        if (!answer.IsSuccess)
        {
            return Report(answer);
        }

        var document = ReadDocument(answer.Body, ApiJson.Default.ConfigurationCheckDocument);
        if (json)
        {
            output.WriteLine(answer.Body.Trim());
            return document?.Status == "valid" ? Success : Failure;
        }

        if (document is null || !IsWhole(document))
        {
            return Report(answer with { Problem = null });
        }

        WriteConfigurationCheck(document);
        return document.Status == "valid" ? Success : Failure;
    }

    private bool IsWhole(ConfigurationCheckDocument check) =>
        check is { Name: not null, BaseBranch: not null, CommitId: not null, Issues: not null }
        && check.Status is "valid" or "invalid" or "missing"
        && (check.Status != "valid" || check.Configuration is { Validation: not null, AllowedEgressHosts: not null, Methodology: not null, Image: not null, Limits: not null })
        && (check.Status != "invalid" || check.Issues.Count > 0)
        && check.Issues.All(issue => issue is { Setting: not null, Id: not null, Arguments: not null } && _catalog.Ids(_language).Contains(issue.Id));

    private void WriteConfigurationCheck(ConfigurationCheckDocument check)
    {
        switch (check.Status)
        {
            case "valid":
                WriteSentence(MessageIds.CliProjectCheckValid, check.Name, check.CommitId, check.BaseBranch);
                WriteConfigurationSettings(check.Configuration!);
                break;
            case "missing":
                WriteSentence(MessageIds.CliProjectCheckMissing, check.Name, check.CommitId, check.BaseBranch);
                break;
            default:
                WriteSentence(MessageIds.CliProjectCheckInvalid, check.Name, check.CommitId, check.BaseBranch, check.Issues.Count);
                var rows = new List<string[]> { new[] { Caption(MessageIds.CliProjectCheckColumnSetting), Caption(MessageIds.CliProjectCheckColumnLine), Caption(MessageIds.CliProjectCheckColumnProblem) } };
                rows.AddRange(check.Issues.Select(issue => new[]
                {
                    ConfigurationText.Safe(issue.Setting, maximumLength: 200),
                    issue.Line?.ToString(CultureInfo.InvariantCulture) ?? Absent,
                    _catalog.Resolve(new Message(issue.Id, [.. issue.Arguments]), _language),
                }));
                TableWriter.Write(output, rows);
                break;
        }
    }

    /// <summary>The settings of a valid file as <c>key: value</c> lines: the keys are the file's and are not translated, and a value that came from the file is made safe to print.</summary>
    private void WriteConfigurationSettings(ProjectConfigurationDocument configuration)
    {
        var lines = new List<(string Key, string? Value)>
        {
            (ProjectConfigurationFile.PrimaryAliasKey, configuration.PrimaryAlias),
            (ProjectConfigurationFile.FallbackAliasKey, configuration.FallbackAlias),
            (ProjectConfigurationFile.EffortKey, configuration.Effort),
            (ProjectConfigurationFile.MethodologyKey, configuration.Methodology),
        };
        lines.AddRange(configuration.Validation.Select(command => (ProjectConfigurationFile.ValidationKey, (string?)ConfigurationText.Line(command))));
        lines.Add((ProjectConfigurationFile.CorrectionRoundsKey, configuration.CorrectionRounds.ToString(CultureInfo.InvariantCulture)));
        lines.Add((ProjectConfigurationFile.ImageKey, configuration.Image));
        lines.Add(($"{ProjectConfigurationFile.LimitsKey}.{ProjectConfigurationFile.TaskDurationKey}", configuration.Limits.TaskDuration));
        lines.Add(($"{ProjectConfigurationFile.LimitsKey}.{ProjectConfigurationFile.CpusKey}", configuration.Limits.Cpus));
        lines.Add(($"{ProjectConfigurationFile.LimitsKey}.{ProjectConfigurationFile.MemoryKey}", configuration.Limits.Memory));
        lines.Add((ProjectConfigurationFile.AllowedEgressHostsKey, configuration.AllowedEgressHosts.Count == 0 ? null : string.Join(", ", configuration.AllowedEgressHosts)));
        lines.Add((ProjectConfigurationFile.LanguageKey, configuration.Language));
        foreach (var (key, value) in lines)
        {
            output.WriteLine($"{key}: {(value is null ? Absent : ConfigurationText.Safe(value, maximumLength: 2000))}");
        }
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
