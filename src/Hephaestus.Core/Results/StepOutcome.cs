using System.Text.Json.Serialization;

namespace Hephaestus.Core.Results;

/// <summary>How a step ended, as the result of every Agent step says it (AD-13); the JSON value is the kebab-case name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StepOutcome>))]
public enum StepOutcome
{
    [JsonStringEnumMemberName("completed")]
    Completed,

    [JsonStringEnumMemberName("needs-decision")]
    NeedsDecision,
}
