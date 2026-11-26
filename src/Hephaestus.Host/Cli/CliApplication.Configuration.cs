using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.CommandLine;
using Hephaestus.Core.Localization;
using Hephaestus.Host.Api;

namespace Hephaestus.Host.Cli;

/// <summary>The commands of the Installation Configuration (Story 1.9): <c>alias</c>, <c>limit</c>, <c>protected-path</c>, <c>setting</c>, and <c>config show</c>.</summary>
internal sealed partial class CliApplication
{
    private const string Absent = "none";

    /// <summary>Runs the command when it is one of the configuration commands; null when it is not.</summary>
    private async Task<int?> RunConfigurationAsync(Command command, ParseResult parse, CommandTree tree, bool json, CancellationToken cancellationToken)
    {
        if (command == tree.AliasAdd)
        {
            var request = new AliasRequest(parse.GetValue(tree.AliasAgent), parse.GetValue(tree.AliasModel), parse.GetValue(tree.AliasEffort));
            var body = JsonSerializer.Serialize(request, ApiJson.Default.AliasRequest);
            return await ShowAsync(HttpMethod.Put, AliasPath(parse.GetValue(tree.AliasAddName)!), body, json, ApiJson.Default.AliasDocument, IsWhole, WriteAliasAdded, cancellationToken);
        }

        if (command == tree.AliasList)
        {
            return await ShowAsync(HttpMethod.Get, "/api/aliases", null, json, ApiJson.Default.AliasListDocument, list => list.Aliases is not null && list.Aliases.All(IsWhole), (list, _) => WriteAliases(list.Aliases), cancellationToken);
        }

        if (command == tree.AliasRemove)
        {
            return await ShowAsync(HttpMethod.Delete, AliasPath(parse.GetValue(tree.AliasRemoveName)!), null, json, ApiJson.Default.AliasDocument, IsWhole, (alias, _) => WriteAliasSentence(alias, MessageIds.CliAliasRemovedModel, MessageIds.CliAliasRemovedEffort), cancellationToken);
        }

        if (command == tree.LimitSet)
        {
            var request = new LimitsRequest(parse.GetValue(tree.LimitTaskDuration), parse.GetValue(tree.LimitCpus), parse.GetValue(tree.LimitMemory));
            var body = JsonSerializer.Serialize(request, ApiJson.Default.LimitsRequest);
            return await ShowAsync(HttpMethod.Put, "/api/limits", body, json, ApiJson.Default.LimitsDocument, limits => limits is not null, (limits, _) => WriteLimits(limits), cancellationToken);
        }

        if (command == tree.LimitShow)
        {
            return await ShowAsync(HttpMethod.Get, "/api/limits", null, json, ApiJson.Default.LimitsDocument, limits => limits is not null, (limits, _) => WriteLimits(limits), cancellationToken);
        }

        if (command == tree.ProtectedPathAdd)
        {
            return await ShowAsync(HttpMethod.Put, ProtectedPathPath(parse.GetValue(tree.ProtectedPathAddGlob)!), null, json, ApiJson.Default.ProtectedPathDocument, path => path.Pattern is not null, (path, status) => WriteSentence(status == HttpStatusCode.Created ? MessageIds.CliProtectedPathAdded : MessageIds.CliProtectedPathAlready, path.Pattern), cancellationToken);
        }

        if (command == tree.ProtectedPathList)
        {
            return await ShowAsync(HttpMethod.Get, "/api/protected-paths", null, json, ApiJson.Default.ProtectedPathListDocument, list => list.ProtectedPaths is not null && list.ProtectedPaths.All(p => p is not null), (list, _) => WriteProtectedPaths(list.ProtectedPaths), cancellationToken);
        }

        if (command == tree.ProtectedPathRemove)
        {
            return await ShowAsync(HttpMethod.Delete, ProtectedPathPath(parse.GetValue(tree.ProtectedPathRemoveGlob)!), null, json, ApiJson.Default.ProtectedPathDocument, path => path.Pattern is not null, (path, _) => WriteSentence(MessageIds.CliProtectedPathRemoved, path.Pattern), cancellationToken);
        }

        if (command == tree.SettingList)
        {
            return await ShowAsync(HttpMethod.Get, "/api/settings", null, json, ApiJson.Default.SettingListDocument, list => list.Settings is not null && list.Settings.All(IsWhole), (list, _) => WriteSettings(list.Settings), cancellationToken);
        }

        if (command == tree.SettingSet)
        {
            var path = "/api/settings/" + Uri.EscapeDataString(parse.GetValue(tree.SettingSetName)!);
            return await ShowAsync(HttpMethod.Put, path, parse.GetValue(tree.SettingSetValue)!, json, ApiJson.Default.SettingDocument, IsWhole, (setting, _) => WriteSentence(MessageIds.CliSettingSetDone, setting.Name, setting.Value), cancellationToken);
        }

        if (command == tree.ConfigShow)
        {
            return await ShowAsync(HttpMethod.Get, "/api/configuration", null, json, ApiJson.Default.ConfigurationDocument, IsWhole, (configuration, _) => WriteConfiguration(configuration), cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Calls the API and prints its answer: a failure as the problem it describes, the document unchanged under <c>--json</c>,
    /// otherwise what <paramref name="write"/> makes of it. An answer that is not the document it should be (<paramref name="isWhole"/> says no: a member missing, a null where there must be a value) is a failure too.
    /// </summary>
    private async Task<int> ShowAsync<T>(HttpMethod method, string path, string? body, bool json, JsonTypeInfo<T> shape, Func<T, bool> isWhole, Action<T, HttpStatusCode> write, CancellationToken cancellationToken)
        where T : class
    {
        var answer = await CallAsync(method, path, body, cancellationToken);
        if (answer is null)
        {
            return Failure;
        }

        if (!answer.IsSuccess)
        {
            return Report(answer);
        }

        if (json)
        {
            output.WriteLine(answer.Body.Trim());
            return Success;
        }

        if (ReadDocument(answer.Body, shape) is not { } document || !isWhole(document))
        {
            return Report(answer with { Problem = null });
        }

        write(document, answer.Status);
        return Success;
    }

    private static T? ReadDocument<T>(string body, JsonTypeInfo<T> shape)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(body, shape);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsWhole(AliasDocument? alias) =>
        alias is { Name: not null, AddedAt: not null }
        && ((alias.Agent is not null && alias.Model is not null && alias.Effort is null) || (alias.Agent is null && alias.Model is null && alias.Effort is not null));

    private static bool IsWhole(SettingDocument? setting) => setting is { Name: not null, Value: not null, Default: not null };

    private static bool IsWhole(ConfigurationDocument configuration) =>
        configuration is { TrustedUsers: not null, Aliases: not null, Limits: not null, ProtectedPaths: not null, Settings: not null, Secrets: not null }
        && configuration.TrustedUsers.All(user => user is { Login: not null, AddedAt: not null })
        && configuration.Aliases.All(IsWhole)
        && configuration.ProtectedPaths.All(path => path is not null)
        && configuration.Settings.All(IsWhole)
        && configuration.Secrets.All(secret => secret is { Name: not null });

    private static string AliasPath(string name) => "/api/aliases/" + Uri.EscapeDataString(name);

    private static string ProtectedPathPath(string glob) => "/api/protected-paths?pattern=" + Uri.EscapeDataString(glob);

    private void WriteSentence(string id, params object[] arguments) => output.WriteLine(_catalog.Resolve(new Message(id, arguments), _language));

    private string Caption(string id) => _catalog.Resolve(id, _language);

    private void WriteAliasAdded(AliasDocument alias, HttpStatusCode status)
    {
        if (status == HttpStatusCode.Created)
        {
            WriteAliasSentence(alias, MessageIds.CliAliasAddedModel, MessageIds.CliAliasAddedEffort);
        }
        else
        {
            WriteAliasSentence(alias, MessageIds.CliAliasAlreadyModel, MessageIds.CliAliasAlreadyEffort);
        }
    }

    private void WriteAliasSentence(AliasDocument alias, string modelId, string effortId)
    {
        if (alias.Effort is { } effort)
        {
            WriteSentence(effortId, alias.Name, effort);
        }
        else
        {
            WriteSentence(modelId, alias.Name, alias.Agent!, alias.Model!);
        }
    }

    private void WriteTrustedUsers(IReadOnlyList<TrustedUserDocument> users)
    {
        if (users.Count == 0)
        {
            output.WriteLine(Caption(MessageIds.CliTrustedUserListEmpty));
            return;
        }

        var rows = new List<string[]> { new[] { Caption(MessageIds.CliTrustedUserColumnLogin), Caption(MessageIds.CliTrustedUserColumnId), Caption(MessageIds.CliTrustedUserColumnAdded) } };
        rows.AddRange(users.Select(u => new[] { u.Login, u.UserId.ToString(CultureInfo.InvariantCulture), u.AddedAt }));
        TableWriter.Write(output, rows);
    }

    private void WriteAliases(IReadOnlyList<AliasDocument> aliases)
    {
        if (aliases.Count == 0)
        {
            output.WriteLine(Caption(MessageIds.CliAliasListEmpty));
            return;
        }

        var rows = new List<string[]> { new[] { Caption(MessageIds.CliAliasColumnName), Caption(MessageIds.CliAliasColumnAgent), Caption(MessageIds.CliAliasColumnModel), Caption(MessageIds.CliAliasColumnEffort), Caption(MessageIds.CliAliasColumnAdded) } };
        rows.AddRange(aliases.Select(a => new[] { a.Name, a.Agent ?? Absent, a.Model ?? Absent, a.Effort ?? Absent, a.AddedAt }));
        TableWriter.Write(output, rows);
    }

    private void WriteLimits(LimitsDocument limits)
    {
        WriteSentence(MessageIds.CliLimitTaskDuration, limits.TaskDuration ?? Absent);
        WriteSentence(MessageIds.CliLimitCpus, limits.Cpus ?? Absent);
        WriteSentence(MessageIds.CliLimitMemory, limits.Memory ?? Absent);
    }

    private void WriteProtectedPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            output.WriteLine(Caption(MessageIds.CliProtectedPathListEmpty));
            return;
        }

        TableWriter.Write(output, [.. paths.Prepend(Caption(MessageIds.CliProtectedPathColumnPattern)).Select(path => new[] { path })]);
    }

    private void WriteSettings(IReadOnlyList<SettingDocument> settings)
    {
        var rows = new List<string[]> { new[] { Caption(MessageIds.CliSettingColumnName), Caption(MessageIds.CliSettingColumnValue), Caption(MessageIds.CliSettingColumnDefault) } };
        rows.AddRange(settings.Select(s => new[] { s.Name, s.Value, s.Default }));
        TableWriter.Write(output, rows);
    }

    private void WriteConfiguration(ConfigurationDocument configuration)
    {
        WriteSection(MessageIds.CliConfigSectionTrustedUsers, () => WriteTrustedUsers(configuration.TrustedUsers), first: true);
        WriteSection(MessageIds.CliConfigSectionAliases, () => WriteAliases(configuration.Aliases));
        WriteSection(MessageIds.CliConfigSectionLimits, () => WriteLimits(configuration.Limits));
        WriteSection(MessageIds.CliConfigSectionProtectedPaths, () => WriteProtectedPaths(configuration.ProtectedPaths));
        WriteSection(MessageIds.CliConfigSectionSettings, () => WriteSettings(configuration.Settings));
        WriteSection(MessageIds.CliConfigSectionSecrets, () => WriteSecretNames(configuration.Secrets.Select(s => s.Name).ToList()));
    }

    private void WriteSecretNames(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            output.WriteLine(Caption(MessageIds.CliSecretListEmpty));
            return;
        }

        foreach (var name in names)
        {
            output.WriteLine(name);
        }
    }

    private void WriteSection(string titleId, Action write, bool first = false)
    {
        if (!first)
        {
            output.WriteLine();
        }

        output.WriteLine(Caption(titleId));
        write();
    }
}
