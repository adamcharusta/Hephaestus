using System.CommandLine;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Secrets;
using Hephaestus.Host.Api;

namespace Hephaestus.Host.Cli;

/// <summary>
/// The command line (AD-17, UX-DR-15, UX-DR-16): parses the arguments, calls the control API, renders its answer. Standard
/// output carries the answer (one JSON document under <c>--json</c>), standard error carries a failure as title then
/// detail; exit code 0 is success, 1 a failure, 2 a command that was not understood. A secret's value is read from standard
/// input and nowhere else (UX "Command Line").
/// </summary>
internal sealed class CliApplication(HostSettings settings, ControlApiClient client, TextReader input, TextWriter output, TextWriter error)
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int NotUnderstood = 2;

    private readonly MessageCatalog _catalog = MessageCatalog.Instance;

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var language = settings.Language;
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

        HelpWriter.Write(command, tree.Help, output, language);
        return Success;
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

        output.WriteLine(_catalog.Resolve(new Message(MessageIds.VersionLine, version), settings.Language));
        return Success;
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
            output.WriteLine(_catalog.Resolve(MessageIds.CliSecretListEmpty, settings.Language));
            return Success;
        }

        output.WriteLine(_catalog.Resolve(MessageIds.CliSecretListHeader, settings.Language));
        foreach (var name in secrets)
        {
            output.WriteLine(name);
        }

        return Success;
    }

    /// <summary>
    /// Everything standard input holds, up to what a value of the longest length and its line break take, plus one character; the API
    /// refuses what is too long. One trailing line break is dropped so that <c>echo value |</c> works. The value is never echoed,
    /// logged, or kept after the request.
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

        output.WriteLine(json ? answer.Body.Trim() : _catalog.Resolve(sentence, settings.Language));
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

    private async Task<ApiAnswer?> CallAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(method, path, body, cancellationToken);
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
        error.WriteLine(_catalog.Resolve(titleId, settings.Language));
        error.WriteLine(_catalog.Resolve(detail, settings.Language));
    }
}
