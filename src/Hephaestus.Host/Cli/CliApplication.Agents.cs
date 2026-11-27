using System.CommandLine;
using System.Net;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Host.Api;

namespace Hephaestus.Host.Cli;

/// <summary>The commands of how each Agent signs in and whether it is ready (Story 3.10): <c>agent set-auth</c>, <c>agent check</c>, and <c>agent list</c>.</summary>
internal sealed partial class CliApplication
{
    /// <summary>Runs the command when it is one of the Agent commands; null when it is not.</summary>
    private async Task<int?> RunAgentAsync(Command command, ParseResult parse, CommandTree tree, bool json, CancellationToken cancellationToken)
    {
        if (command == tree.AgentSetAuth)
        {
            // The credential is read from standard input and goes to the API in the body, never in an address; an empty input keeps the credential that is stored.
            var credential = await ReadValueAsync(cancellationToken);
            var kept = string.IsNullOrWhiteSpace(credential);
            var path = $"{AgentPath(parse.GetValue(tree.AgentSetAuthAgent)!)}/authentication?mode={Uri.EscapeDataString(parse.GetValue(tree.AgentMode)!)}";
            return await ShowAsync(HttpMethod.Put, path, credential, json, ApiJson.Default.AgentDocument, agent => IsWhole(agent) && agent.Mode is not null && agent.Secret is not null, (agent, _) => WriteSentence(kept ? MessageIds.CliAgentSetAuthKept : MessageIds.CliAgentSetAuthDone, agent.Agent, agent.Mode!, agent.Secret!), cancellationToken);
        }

        if (command == tree.AgentList)
        {
            return await ShowAsync(HttpMethod.Get, "/api/agents", null, json, ApiJson.Default.AgentListDocument, list => list.Agents is not null && list.Agents.All(IsWhole), (list, _) => WriteAgents(list.Agents), cancellationToken);
        }

        return command == tree.AgentCheck ? await CheckAgentAsync(parse.GetValue(tree.AgentCheckAgent)!, json, cancellationToken) : null;
    }

    /// <summary>
    /// Runs the probe and says what it found. Exit code 0 means the Agent is ready: an Agent that is unavailable is the answer to the question and is written to standard output, and it ends the command
    /// with 1, so that a script can rely on the check.
    /// </summary>
    private async Task<int> CheckAgentAsync(string agent, bool json, CancellationToken cancellationToken)
    {
        var answer = await CallAsync(HttpMethod.Post, $"{AgentPath(agent)}/check", body: null, cancellationToken, ControlApiClient.CheckTimeout);
        if (answer is null)
        {
            return Failure;
        }

        if (!answer.IsSuccess)
        {
            return Report(answer);
        }

        var document = ReadDocument(answer.Body, ApiJson.Default.AgentDocument);
        if (json)
        {
            output.WriteLine(answer.Body.Trim());
            return document?.Status == "ready" ? Success : Failure;
        }

        if (document is null || !IsWhole(document) || document.Status is not ("ready" or "unavailable") || document.CheckedAt is null)
        {
            return Report(answer with { Problem = null });
        }

        if (document.Status == "ready")
        {
            WriteSentence(MessageIds.CliAgentCheckReady, document.Agent, document.CheckedAt);
            return Success;
        }

        WriteSentence(MessageIds.CliAgentCheckUnavailable, document.Agent, document.CheckedAt, ReasonOf(document));
        return Failure;
    }

    private static string AgentPath(string agent) => "/api/agents/" + Uri.EscapeDataString(agent);

    /// <summary>Whether an Agent document is what the API makes: a mode comes with its secret and when it was chosen, a finding with its time, and a reason names a sentence this installation can write.</summary>
    private bool IsWhole(AgentDocument? agent) =>
        agent is { Agent: not null }
        && (agent.Mode is null) == (agent.ChosenAt is null)
        && (agent.Mode is null) == (agent.Secret is null)
        && (agent.Mode is null) == (agent.CredentialStored is null)
        && (agent.Mode is not null || agent.Status is null)
        && (agent.Status is null) == (agent.CheckedAt is null)
        && agent.Status is null or "ready" or "unavailable"
        && (agent.Reason is null || (agent.Reason.Arguments is not null && _catalog.Ids(_language).Contains(agent.Reason.Id)));

    private string ReasonOf(AgentDocument agent) =>
        agent.Reason is { } reason ? _catalog.Resolve(new Message(reason.Id, [.. reason.Arguments]), _language) : Absent;

    private void WriteAgents(IReadOnlyList<AgentDocument> agents)
    {
        var rows = new List<string[]>
        {
            new[] { Caption(MessageIds.CliAgentColumnAgent), Caption(MessageIds.CliAgentColumnMode), Caption(MessageIds.CliAgentColumnCredential), Caption(MessageIds.CliAgentColumnStatus), Caption(MessageIds.CliAgentColumnChecked), Caption(MessageIds.CliAgentColumnReason) },
        };
        rows.AddRange(agents.Select(agent => new[]
        {
            agent.Agent,
            agent.Mode ?? Absent,
            agent.CredentialStored is { } stored ? Caption(stored ? MessageIds.CliAgentCredentialStored : MessageIds.CliAgentCredentialMissing) : Absent,
            agent.Status ?? Absent,
            agent.CheckedAt ?? Absent,
            agent.Reason is null ? Absent : ReasonOf(agent),
        }));
        TableWriter.Write(output, rows);
    }
}
