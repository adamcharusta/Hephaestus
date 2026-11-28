using System.Globalization;
using System.Text;
using System.Text.Json;
using Hephaestus.Core.Escalations;

namespace Hephaestus.Core.Results;

/// <summary>What a step's result says, as it is recorded on the step run, and the reading of the result document that decides it (AD-13).</summary>
public static class StepOutcomes
{
    /// <summary>The Agent did what it was asked.</summary>
    public const string Completed = "completed";

    /// <summary>The Agent needs a decision of the Owner before it can go on.</summary>
    public const string NeedsDecision = "needs-decision";

    /// <summary>
    /// Reads the result document of an Implementation by strict deserialization, as the schema allows it: no unknown member, an outcome that is one of the two, a summary, and, for a result that needs a decision, at least one question
    /// with a text. Anything else is no result, and is never interpreted. Null when the document is missing or is not such a result.
    /// </summary>
    public static ImplementationResult? Read(string? document)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize(document, ResultJson.Default.ImplementationResult);
            if (result is null || result.Summary is null || result.Questions is null || result.Questions.Any(question => string.IsNullOrWhiteSpace(question.Question) || question.Options is null))
            {
                return null;
            }

            if (result.Outcome == StepOutcome.NeedsDecision)
            {
                // A decision the Owner cannot be asked is no result: it is read now, so that it is an error of the step and not of the loop later.
                _ = result.Questions.Count == 0 ? throw new ArgumentException("A result that needs a decision asks something.") : Request(result);
            }

            return result;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The Escalation a result that needs a decision raises (FR-48): the one question with its options, or, when the Agent asked several, the questions in one text, numbered, with no options, because the Owner answers them together in one reply.
    /// </summary>
    public static EscalationRequest Request(ImplementationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Questions.Count == 1)
        {
            return EscalationRequest.For(result.Questions[0].Question, result.Questions[0].Options);
        }

        var decision = new StringBuilder();
        for (var index = 0; index < result.Questions.Count; index++)
        {
            decision.Append(CultureInfo.InvariantCulture, $"{index + 1}. {result.Questions[index].Question} ");
        }

        return EscalationRequest.For(decision.ToString(), []);
    }
}
