namespace Hephaestus.Core.Aliases;

/// <summary>The Agents Hephaestus drives (the Glossary's Agent): Claude Code and Codex CLI, and no other in v1.</summary>
public enum Agent
{
    ClaudeCode,
    CodexCli,
}

public static class Agents
{
    /// <summary>The name a command, a document, or a configuration file carries; never translated.</summary>
    public static string Tag(this Agent agent) => agent switch
    {
        Agent.ClaudeCode => "claude-code",
        Agent.CodexCli => "codex-cli",
        _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, null),
    };

    public static bool TryParse(string? tag, out Agent agent)
    {
        switch (tag?.Trim().ToLowerInvariant())
        {
            case "claude-code":
                agent = Agent.ClaudeCode;
                return true;
            case "codex-cli":
                agent = Agent.CodexCli;
                return true;
            default:
                agent = default;
                return false;
        }
    }

    /// <summary>The tags in the order a message lists them.</summary>
    public static IReadOnlyList<string> Tags { get; } = [.. Enum.GetValues<Agent>().Select(Tag)];
}
