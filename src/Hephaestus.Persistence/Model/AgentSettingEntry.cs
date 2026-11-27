namespace Hephaestus.Persistence.Model;

/// <summary>How an Agent signs in (FR-25): the Agent's tag is the key, the mode is its tag, and the time is when the Owner chose it. The credential is a secret, and is not here.</summary>
internal sealed class AgentSettingEntry
{
    public string Agent { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public DateTimeOffset ChosenAt { get; set; }
}
