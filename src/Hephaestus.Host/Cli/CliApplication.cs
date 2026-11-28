using System.CommandLine;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Secrets;
using Hephaestus.Core.Settings;
using Hephaestus.Host.Api;

namespace Hephaestus.Host.Cli;

/// <summary>
/// The command line (AD-17, UX-DR-15, UX-DR-16): parses the arguments, calls the control API, renders its answer. Standard
/// output carries the answer (one JSON document under <c>--json</c>), standard error carries a failure as title then
/// detail; exit code 0 is success, 1 a failure, 2 a command that was not understood. A secret's value is read from standard
/// input and nowhere else (UX "Command Line").
/// </summary>
internal sealed partial class CliApplication(HostSettings settings, ControlApiClient client, TextReader input, TextWriter output, TextWriter error, TimeSpan? signalWait = null, TimeSpan? signalPoll = null)
{
    private readonly TimeSpan _signalWait = signalWait ?? DefaultSignalWait;
    private readonly TimeSpan _signalPoll = signalPoll ?? DefaultSignalPoll;

    public const int Success = 0;
    public const int Failure = 1;
    public const int NotUnderstood = 2;

    /// <summary>The longest the command line waits for the stored language, so that a host that accepts and never answers delays no command by the client's whole timeout.</summary>
    public static readonly TimeSpan LanguageLookupTimeout = TimeSpan.FromSeconds(2);

    private readonly MessageCatalog _catalog = MessageCatalog.Instance;
    private Language _language = settings.Language;

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var language = _language = await ResolveLanguageAsync(cancellationToken);
        client.UseLanguage(language);
        var tree = new CommandTree(language);

        if (args.Length == 0)
        {
            HelpWriter.Write(tree.Root, tree.Help, output, language);
            return Success;
        }

        var parse = tree.Root.Parse(args);
        var help = parse.GetValue(tree.Help);
        if (!help && AsksForSecretValue(parse, tree, args))
        {
            // Whatever follows the name may be the value itself, and a token like --value=... may have been taken for the name (a name
            // never starts with a hyphen), so the refusal names no token.
            WriteFailure(MessageIds.CliSecretValueArgumentTitle, new Message(MessageIds.CliSecretValueArgumentDetail));
            return NotUnderstood;
        }

        // The same for the App's private key: any token that is not understood may be the key, so none is echoed, not even beside --help.
        if (parse.CommandResult.Command == tree.GitHubConnect && (parse.UnmatchedTokens.Count > 0 || (!help && parse.Errors.Count > 0)))
        {
            WriteFailure(MessageIds.CliGitHubConnectArgumentsTitle, new Message(MessageIds.CliGitHubConnectArgumentsDetail));
            return NotUnderstood;
        }

        // The same for an Agent's credential, which `agent set-auth` reads from standard input: a token that is not understood may have been the credential, so none is echoed.
        if (parse.CommandResult.Command == tree.AgentSetAuth && (parse.UnmatchedTokens.Count > 0 || (!help && parse.Errors.Count > 0)))
        {
            WriteFailure(MessageIds.CliAgentSetAuthArgumentsTitle, new Message(MessageIds.CliAgentSetAuthArgumentsDetail));
            return NotUnderstood;
        }

        // Help is answered even when a required argument is missing: `secret set --help` has no name to give.
        if (parse.UnmatchedTokens.Count > 0 || (parse.Errors.Count > 0 && !help))
        {
            return NotUnderstoodError(parse, args);
        }

        if (help)
        {
            HelpWriter.Write(parse.CommandResult.Command, tree.Help, output, language);
            return Success;
        }

        if (parse.CommandResult.Command == tree.Version)
        {
            return await VersionAsync(parse.GetValue(tree.Json), cancellationToken);
        }

        var command = parse.CommandResult.Command;
        var json = parse.GetValue(tree.Json);
        if (command == tree.Health)
        {
            return await HealthAsync(json, cancellationToken);
        }

        if (command == tree.SecretSet)
        {
            return await SetSecretAsync(parse.GetValue(tree.SecretSetName)!, json, cancellationToken);
        }

        if (command == tree.SecretList)
        {
            return await ListSecretsAsync(json, cancellationToken);
        }

        if (command == tree.SecretRemove)
        {
            return await RemoveSecretAsync(parse.GetValue(tree.SecretRemoveName)!, json, cancellationToken);
        }

        if (command == tree.GitHubConnect)
        {
            return await ConnectGitHubAsync(parse.GetValue(tree.GitHubAppId), json, cancellationToken);
        }

        if (command == tree.GitHubCheck)
        {
            return await CheckGitHubAsync(json, cancellationToken);
        }

        if (command == tree.TrustedUserAdd)
        {
            return await AddTrustedUserAsync(parse.GetValue(tree.TrustedUserAddLogin)!, json, cancellationToken);
        }

        if (command == tree.TrustedUserList)
        {
            return await ListTrustedUsersAsync(json, cancellationToken);
        }

        if (command == tree.TrustedUserRemove)
        {
            return await RemoveTrustedUserAsync(parse.GetValue(tree.TrustedUserRemoveLogin)!, json, cancellationToken);
        }

        if (await RunConfigurationAsync(command, parse, tree, json, cancellationToken) is { } configured)
        {
            return configured;
        }

        if (await RunAgentAsync(command, parse, tree, json, cancellationToken) is { } agent)
        {
            return agent;
        }

        if (command == tree.SignalShow)
        {
            return await RunSignalCommandAsync(command, parse, tree, json, cancellationToken) ?? Failure;
        }

        HelpWriter.Write(command, tree.Help, output, language);
        return Success;
    }

    /// <summary>
    /// The language this run writes in: the one the Owner stored, asked of the host before anything is parsed. When none is stored, or
    /// the host cannot be asked, does not answer within <see cref="LanguageLookupTimeout"/>, or does not answer as expected, it is the
    /// environment's (English by default); the command's own call then reports what is wrong.
    /// </summary>
    private async Task<Language> ResolveLanguageAsync(CancellationToken cancellationToken)
    {
        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lookup.CancelAfter(LanguageLookupTimeout);
        try
        {
            var answer = await client.GetAsync("/api/settings", lookup.Token);
            var stored = answer.IsSuccess ? ReadDocument(answer.Body, ApiJson.Default.SettingListDocument)?.Settings : null;
            var language = stored?.FirstOrDefault(setting => setting?.Name == InstallationSettings.LanguageName && setting.IsSet)?.Value;
            return Languages.TryParse(language, out var parsed) ? parsed : settings.Language;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return settings.Language;
        }
    }

    /// <summary>
    /// Whether <c>secret set</c> was given something that may be the value: a token left over, any failure of the parse, or an
    /// option it does not have (<c>--json=value</c> is read as the option <c>--json</c> and the argument <c>value</c>, which would
    /// make the value a name). Only <c>secret set</c> alone has nothing to hide.
    /// </summary>
    private static bool AsksForSecretValue(ParseResult parse, CommandTree tree, string[] args) =>
        parse.CommandResult.Command == tree.SecretSet
        && args.Length > 2
        && (parse.Errors.Count > 0
            || parse.UnmatchedTokens.Count > 0
            || args.Skip(2).Any(token => token.StartsWith('-') && token is not ("--json" or "--help" or "-h")));

    private async Task<int> VersionAsync(bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync("/api/version", cancellationToken);
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

        var version = ReadVersion(answer.Body);
        if (version is null)
        {
            return Report(answer with { Problem = null });
        }

        output.WriteLine(_catalog.Resolve(new Message(MessageIds.VersionLine, version), _language));
        return Success;
    }

    /// <summary>Prints whether the loop is alive; a loop that is not alive is exit code 1, so that a script can ask.</summary>
    private async Task<int> HealthAsync(bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync("/api/health", cancellationToken);
        if (answer is null)
        {
            return Failure;
        }

        if (!answer.IsSuccess)
        {
            return Report(answer);
        }

        var loop = ReadDocument(answer.Body, ApiJson.Default.HealthDocument)?.Loop;
        if (loop is null)
        {
            return Report(answer with { Problem = null });
        }

        if (json)
        {
            output.WriteLine(answer.Body.Trim());
        }
        else
        {
            var line = (loop.Alive, loop.LastTickAt) switch
            {
                (true, { } lastTick) => new Message(MessageIds.CliHealthAlive, lastTick),
                (_, { } lastTick) => new Message(MessageIds.CliHealthStalled, lastTick),
                _ => new Message(MessageIds.CliHealthNeverTicked),
            };
            output.WriteLine(_catalog.Resolve(line, _language));
        }

        return loop.Alive ? Success : Failure;
    }

    private async Task<int> SetSecretAsync(string name, bool json, CancellationToken cancellationToken)
    {
        var value = await ReadValueAsync(cancellationToken);
        var answer = await CallAsync(HttpMethod.Put, SecretPath(name), value, cancellationToken);
        return answer is null ? Failure : Render(answer, json, new Message(MessageIds.CliSecretSetDone, name));
    }

    private async Task<int> RemoveSecretAsync(string name, bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync(HttpMethod.Delete, SecretPath(name), body: null, cancellationToken);
        return answer is null ? Failure : Render(answer, json, new Message(MessageIds.CliSecretRemoveDone, name));
    }

    private async Task<int> ListSecretsAsync(bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync(HttpMethod.Get, "/api/secrets", body: null, cancellationToken);
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

        var secrets = ReadSecrets(answer.Body);
        if (secrets is null)
        {
            return Report(answer with { Problem = null });
        }

        if (secrets.Count == 0)
        {
            output.WriteLine(_catalog.Resolve(MessageIds.CliSecretListEmpty, _language));
            return Success;
        }

        output.WriteLine(_catalog.Resolve(MessageIds.CliSecretListHeader, _language));
        foreach (var name in secrets)
        {
            output.WriteLine(name);
        }

        return Success;
    }

    private async Task<int> AddTrustedUserAsync(string login, bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync(HttpMethod.Put, TrustedUserPath(login), body: null, cancellationToken);
        if (answer is null)
        {
            return Failure;
        }

        var created = answer.Status == System.Net.HttpStatusCode.Created;
        return RenderTrustedUser(answer, json, created ? MessageIds.CliTrustedUserAdded : MessageIds.CliTrustedUserAlready);
    }

    private async Task<int> RemoveTrustedUserAsync(string login, bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync(HttpMethod.Delete, TrustedUserPath(login), body: null, cancellationToken);
        return answer is null ? Failure : RenderTrustedUser(answer, json, MessageIds.CliTrustedUserRemoved);
    }

    private int RenderTrustedUser(ApiAnswer answer, bool json, string sentenceId)
    {
        if (!answer.IsSuccess)
        {
            return Report(answer);
        }

        if (json)
        {
            output.WriteLine(answer.Body.Trim());
            return Success;
        }

        var user = ReadTrustedUser(answer.Body);
        if (user is null)
        {
            return Report(answer with { Problem = null });
        }

        output.WriteLine(_catalog.Resolve(new Message(sentenceId, user.Login, user.UserId), _language));
        return Success;
    }

    private async Task<int> ListTrustedUsersAsync(bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync(HttpMethod.Get, "/api/trusted-users", body: null, cancellationToken);
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

        var users = ReadTrustedUsers(answer.Body);
        if (users is null)
        {
            return Report(answer with { Problem = null });
        }

        WriteTrustedUsers(users);
        return Success;
    }

    private static string TrustedUserPath(string login) => "/api/trusted-users/" + Uri.EscapeDataString(login);

    private static TrustedUserDocument? ReadTrustedUser(string body)
    {
        try
        {
            var user = JsonSerializer.Deserialize(body, ApiJson.Default.TrustedUserDocument);
            return user is { Login: not null, AddedAt: not null } ? user : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<TrustedUserDocument>? ReadTrustedUsers(string body)
    {
        try
        {
            var users = JsonSerializer.Deserialize(body, ApiJson.Default.TrustedUserListDocument)?.TrustedUsers;
            return users is null || users.Any(u => u is not { Login: not null, AddedAt: not null }) ? null : [.. users];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<int> ConnectGitHubAsync(long appId, bool json, CancellationToken cancellationToken)
    {
        var privateKey = await ReadValueAsync(cancellationToken);
        var answer = await CallAsync(HttpMethod.Put, "/api/github/app/" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture), privateKey, cancellationToken);
        return answer is null ? Failure : RenderGitHubIdentity(answer, json, connected: true);
    }

    private async Task<int> CheckGitHubAsync(bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync(HttpMethod.Get, "/api/github/identity", body: null, cancellationToken);
        return answer is null ? Failure : RenderGitHubIdentity(answer, json, connected: false);
    }

    private int RenderGitHubIdentity(ApiAnswer answer, bool json, bool connected)
    {
        if (!answer.IsSuccess)
        {
            return Report(answer);
        }

        if (json)
        {
            output.WriteLine(answer.Body.Trim());
            return Success;
        }

        var identity = ReadGitHubIdentity(answer.Body);
        if (identity is null)
        {
            return Report(answer with { Problem = null });
        }

        GitHubReport.Write(identity, connected, _catalog, _language, output);
        return Success;
    }

    private static GitHubIdentityDocument? ReadGitHubIdentity(string body)
    {
        try
        {
            var identity = JsonSerializer.Deserialize(body, ApiJson.Default.GitHubIdentityDocument);
            return identity is { Installations: not null, Permissions: not null, Warnings: not null } ? identity : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Everything standard input holds, up to what a value of the longest length and its line break take, plus one character; the API
    /// refuses what is too long. One trailing line break is dropped so that <c>echo value |</c> works. The value is never echoed,
    /// logged, or kept after the request. It is a secret's value, the GitHub App's private key, or an Agent's credential.
    /// </summary>
    private async Task<string> ReadValueAsync(CancellationToken cancellationToken)
    {
        var buffer = new char[SecretPolicy.MaximumValueLength + 3];
        var count = await input.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        return SecretPolicy.TrimTrailingLineBreak(new string(buffer, 0, count));
    }

    private static string SecretPath(string name) => "/api/secrets/" + Uri.EscapeDataString(name);

    private int Render(ApiAnswer answer, bool json, Message sentence)
    {
        if (!answer.IsSuccess)
        {
            return Report(answer);
        }

        output.WriteLine(json ? answer.Body.Trim() : _catalog.Resolve(sentence, _language));
        return Success;
    }

    private static List<string>? ReadSecrets(string body)
    {
        try
        {
            var secrets = JsonSerializer.Deserialize(body, ApiJson.Default.SecretListDocument)?.Secrets;
            return secrets is null || secrets.Any(s => s is null) ? null : secrets.Select(s => s.Name).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadVersion(string body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, ApiJson.Default.VersionDocument)?.Version;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task<ApiAnswer?> CallAsync(string path, CancellationToken cancellationToken) => CallAsync(HttpMethod.Get, path, body: null, cancellationToken);

    private async Task<ApiAnswer?> CallAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        try
        {
            return await client.SendAsync(method, path, body, cancellationToken, timeout);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            WriteFailure(MessageIds.CliUnreachableTitle, new Message(MessageIds.CliUnreachableDetail, settings.ControlSocketPath));
            return null;
        }
    }

    private int Report(ApiAnswer answer)
    {
        if (answer.Problem is { } problem)
        {
            error.WriteLine(problem.Title);
            error.WriteLine(problem.Detail);
        }
        else
        {
            WriteFailure(MessageIds.CliBadAnswerTitle, new Message(MessageIds.CliBadAnswerDetail, (int)answer.Status));
        }

        return Failure;
    }

    private int NotUnderstoodError(ParseResult parse, string[] args)
    {
        // The parser's own wording is library prose, so it is not shown; the first token it could not match is.
        var token = parse.UnmatchedTokens.Count > 0 ? parse.UnmatchedTokens[0] : string.Join(' ', args);
        WriteFailure(MessageIds.CliNotUnderstoodTitle, new Message(MessageIds.CliNotUnderstoodUnmatched, token));
        return NotUnderstood;
    }

    private void WriteFailure(string titleId, Message detail)
    {
        error.WriteLine(_catalog.Resolve(titleId, _language));
        error.WriteLine(_catalog.Resolve(detail, _language));
    }
}
