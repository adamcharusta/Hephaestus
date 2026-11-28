using System.Net;
using System.Text.Json;
using System.CommandLine;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Signals;
using Hephaestus.Host.Api;

namespace Hephaestus.Host.Cli;

/// <summary>
/// The commands that send a Signal and the one that reads its status (Story 3.14, AD-17): <c>project start</c>, <c>project stop</c>, and <c>signal show</c>. A command that sends a Signal prints its id, then waits up to ten seconds
/// for the loop to apply it and says whether it was applied or rejected; with <c>--no-wait</c> it returns at once, and a Signal still pending after the wait is reported as pending with the command that follows it (exit code 0).
/// </summary>
internal sealed partial class CliApplication
{
    /// <summary>How long a command that sent a Signal waits to see it applied.</summary>
    public static readonly TimeSpan DefaultSignalWait = TimeSpan.FromSeconds(10);

    /// <summary>How often the command asks while it waits.</summary>
    public static readonly TimeSpan DefaultSignalPoll = TimeSpan.FromMilliseconds(200);

    private async Task<int?> RunSignalCommandAsync(Command command, ParseResult parse, CommandTree tree, bool json, CancellationToken cancellationToken)
    {
        if (command == tree.ProjectStart)
        {
            return await RequestWindowAsync(start: true, parse.GetValue(tree.ProjectStartName)!, parse.GetValue(tree.NoWait), json, cancellationToken);
        }

        if (command == tree.ProjectStop)
        {
            return await RequestWindowAsync(start: false, parse.GetValue(tree.ProjectStopName)!, parse.GetValue(tree.NoWait), json, cancellationToken);
        }

        if (command == tree.SignalShow)
        {
            return await ShowSignalAsync(parse.GetValue(tree.SignalShowId)!, json, cancellationToken);
        }

        return null;
    }

    private async Task<int> RequestWindowAsync(bool start, string name, bool noWait, bool json, CancellationToken cancellationToken)
    {
        var path = (start ? "/api/projects/start" : "/api/projects/stop") + "?name=" + Uri.EscapeDataString(name);
        var answer = await CallAsync(HttpMethod.Post, path, body: null, cancellationToken);
        if (answer is null)
        {
            return Failure;
        }

        if (!answer.IsSuccess)
        {
            return Report(answer);
        }

        if (ReadDocument(answer.Body, ApiJson.Default.SignalDocument) is not { Kind: not null, Status: not null } accepted)
        {
            return Report(answer with { Problem = null });
        }

        if (noWait)
        {
            WriteSignalAppended(accepted, answer.Body, json);
            return Success;
        }

        // The loop applies a Signal within a tick, so the wait is short; one that is still pending when it ends is reported as pending, not as a failure.
        var settled = accepted;
        var waited = TimeSpan.Zero;
        while (settled.Status == SignalStatuses.Tag(SignalStatus.Pending) && waited < _signalWait)
        {
            await Task.Delay(_signalPoll, cancellationToken);
            waited += _signalPoll;
            var current = await CallAsync(HttpMethod.Get, $"/api/signals/{accepted.Id}", body: null, cancellationToken);
            if (current is null)
            {
                return Failure;
            }

            if (!current.IsSuccess)
            {
                return Report(current);
            }

            settled = ReadDocument(current.Body, ApiJson.Default.SignalDocument) is { Kind: not null, Status: not null } read ? read : settled;
        }

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(settled, ApiJson.Default.SignalDocument));
            return settled.Status == SignalStatuses.Tag(SignalStatus.Rejected) ? Failure : Success;
        }

        WriteSentence(MessageIds.CliSignalAppended, accepted.Id);
        if (SignalStatuses.TryParse(settled.Status, out var status) && status == SignalStatus.Rejected)
        {
            error.WriteLine(_catalog.Resolve(new Message(MessageIds.CliSignalRejected, settled.Id, settled.Reason ?? Absent), _language));
            return Failure;
        }

        var sentence = OutcomeOf(start, settled);
        WriteSentence(sentence, sentence == MessageIds.CliSignalPending ? settled.Id : name);
        return Success;
    }

    /// <summary>The sentence that says what an applied request did, or that it is still pending.</summary>
    private static string OutcomeOf(bool start, SignalDocument settled)
    {
        if (settled.Status == SignalStatuses.Tag(SignalStatus.Pending))
        {
            return MessageIds.CliSignalPending;
        }

        return start
            ? settled.Reason == SignalNotes.WindowAlreadyOpen ? MessageIds.CliProjectWindowAlreadyOpen : MessageIds.CliProjectWindowOpened
            : settled.Reason == SignalNotes.WindowNotOpen ? MessageIds.CliProjectWindowNotOpen : MessageIds.CliProjectWindowClosed;
    }

    private void WriteSignalAppended(SignalDocument accepted, string body, bool json)
    {
        if (json)
        {
            output.WriteLine(body.Trim());
            return;
        }

        WriteSentence(MessageIds.CliSignalAppended, accepted.Id);
    }

    private async Task<int> ShowSignalAsync(string id, bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync(HttpMethod.Get, "/api/signals/" + Uri.EscapeDataString(id), body: null, cancellationToken);
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

        if (ReadDocument(answer.Body, ApiJson.Default.SignalDocument) is not { Kind: not null, Status: not null } signal)
        {
            return Report(answer with { Problem = null });
        }

        // Single items are name: value lines; the names are the document's keys and are not translated.
        output.WriteLine($"id: {signal.Id}");
        output.WriteLine($"kind: {signal.Kind}");
        output.WriteLine($"status: {signal.Status}");
        output.WriteLine($"reason: {signal.Reason ?? Absent}");
        return Success;
    }
}
