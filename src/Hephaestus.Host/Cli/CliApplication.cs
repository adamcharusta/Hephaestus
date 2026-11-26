using System.CommandLine;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Host.Api;

namespace Hephaestus.Host.Cli;

/// <summary>
/// The command line (AD-17, UX-DR-15, UX-DR-16): parses the arguments, calls the control API, renders its answer. Standard
/// output carries the answer (one JSON document under <c>--json</c>), standard error carries a failure as title then
/// detail; exit code 0 is success, 1 a failure, 2 a command that was not understood.
/// </summary>
internal sealed class CliApplication(HostSettings settings, ControlApiClient client, TextWriter output, TextWriter error)
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
        if (parse.Errors.Count > 0 || parse.UnmatchedTokens.Count > 0)
        {
            return NotUnderstoodError(parse, args);
        }

        if (parse.GetValue(tree.Help))
        {
            HelpWriter.Write(parse.CommandResult.Command, tree.Help, output, language);
            return Success;
        }

        if (parse.CommandResult.Command == tree.Version)
        {
            return await VersionAsync(parse.GetValue(tree.Json), cancellationToken);
        }

        HelpWriter.Write(parse.CommandResult.Command, tree.Help, output, language);
        return Success;
    }

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

    private async Task<ApiAnswer?> CallAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetAsync(path, cancellationToken);
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
