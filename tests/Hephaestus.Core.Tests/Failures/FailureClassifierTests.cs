using Hephaestus.Core.Failures;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Failures;

/// <summary>AD-11, NFR-11: the class of a failed step is decided in one order, the runtime's cause first, then the adapter's reading of a CLI that ended by itself, then Core's reading of the result, and what no one identified is unknown.</summary>
public class FailureClassifierTests
{
    [Theory]
    [InlineData(StepExitCause.Timeout, FailureClass.Timeout)]
    [InlineData(StepExitCause.OutOfMemory, FailureClass.InfrastructureError)]
    [InlineData(StepExitCause.Lost, FailureClass.InfrastructureError)]
    public void The_runtimes_cause_names_a_timeout_and_an_out_of_memory_or_lost_container_as_an_infrastructure_error(StepExitCause cause, FailureClass expected)
    {
        Assert.Equal(expected, FailureClassifier.FromRuntime(cause));
    }

    [Fact]
    public void A_container_that_ended_by_itself_has_no_class_from_the_runtime()
    {
        Assert.Null(FailureClassifier.FromRuntime(StepExitCause.Exited));
    }

    [Theory]
    [InlineData(StepExitCause.Timeout, FailureClass.Timeout)]
    [InlineData(StepExitCause.OutOfMemory, FailureClass.InfrastructureError)]
    [InlineData(StepExitCause.Lost, FailureClass.InfrastructureError)]
    public void The_runtime_decides_before_the_adapter_and_the_result_are_asked_anything(StepExitCause cause, FailureClass expected)
    {
        var failure = FailureClassifier.Decide(cause, () => throw new InvalidOperationException("the stream is not read"), () => throw new InvalidOperationException("the result is not read"));

        Assert.Equal(expected, failure);
    }

    [Theory]
    [InlineData(FailureClass.AgentError)]
    [InlineData(FailureClass.Unknown)]
    [InlineData(FailureClass.UsageLimit)]
    public void The_adapters_reading_of_a_CLI_that_ended_by_itself_decides_before_the_result_is_looked_at(FailureClass reading)
    {
        var failure = FailureClassifier.Decide(StepExitCause.Exited, () => reading, () => throw new InvalidOperationException("the result is not read"));

        Assert.Equal(reading, failure);
    }

    [Fact]
    public void A_CLI_that_ended_well_with_a_result_that_is_missing_or_not_whole_is_an_agent_error_and_with_a_whole_one_did_not_fail()
    {
        Assert.Equal(FailureClass.AgentError, FailureClassifier.Decide(StepExitCause.Exited, () => null, () => false));
        Assert.Null(FailureClassifier.Decide(StepExitCause.Exited, () => null, () => true));
    }

    [Fact]
    public void An_answer_is_asked_for_only_when_the_question_reaches_it()
    {
        var asked = new List<string>();

        FailureClassifier.Decide(StepExitCause.Exited, () => { asked.Add("adapter"); return null; }, () => { asked.Add("result"); return true; });

        Assert.Equal(["adapter", "result"], asked);
        Assert.Throws<ArgumentNullException>(() => FailureClassifier.Decide(StepExitCause.Exited, null!, () => true));
        Assert.Throws<ArgumentNullException>(() => FailureClassifier.Decide(StepExitCause.Exited, () => null, null!));
    }

    [Theory]
    [InlineData(FailureClass.Timeout, true)]
    [InlineData(FailureClass.AgentError, true)]
    [InlineData(FailureClass.InfrastructureError, true)]
    [InlineData(FailureClass.Unknown, true)]
    [InlineData(FailureClass.ValidationFailure, false)]
    [InlineData(FailureClass.UsageLimit, false)]
    [InlineData(FailureClass.Cancelled, false)]
    public void Only_four_classes_spend_the_step_retry_bound(FailureClass failure, bool spends)
    {
        Assert.Equal(spends, failure.SpendsStepRetry());
    }
}
