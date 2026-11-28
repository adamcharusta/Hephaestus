using System.Text.Json;
using Hephaestus.Core.Context;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Methodologies;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Validation;

/// <summary>Story 3.12, AD-13, AD-11, FR-31, FR-19: Validation passes only when every command exited with status 0, and only what the runtime saw of the containers decides it.</summary>
public class ValidationRunTests
{
    private static readonly string[] Commands = ["dotnet build", "dotnet test", "npm run lint"];

    private static Guid Run(int number) => Guid.Parse($"0197a8a0-0000-7000-8000-0000000001{number:D2}");

    private static ValidationCommandResult Ended(int index, int exitCode, StepExitCause cause = StepExitCause.Exited, int seconds = 3) =>
        ValidationCommandResult.Ended(index, Commands[index], Run(index), exitCode, cause, TimeSpan.FromSeconds(seconds));

    private static ValidationRun Passing(int count)
    {
        var run = ValidationRun.Begin(Commands);
        for (var index = 0; index < count; index++)
        {
            run = run.Record(Ended(index, 0));
        }

        return run;
    }

    [Fact]
    public void Validation_passes_only_when_every_command_exited_with_status_0()
    {
        var run = Passing(3);

        Assert.True(run.IsDecided);
        Assert.Equal(ValidationOutcome.Passed, run.Outcome);
        Assert.Null(run.NextIndex);
        Assert.Null(run.NextCommand);
        Assert.All(run.Results, result => Assert.Equal(ValidationCommandStatus.Passed, result.Status));
        Assert.Null(ValidationOutcome.Passed.Failure());
        Assert.Equal("passed", ValidationOutcome.Passed.Tag());
    }

    [Fact]
    public void Commands_are_started_one_at_a_time_in_order_and_Validation_is_undecided_until_the_last_passes()
    {
        var run = ValidationRun.Begin(Commands);

        for (var index = 0; index < Commands.Length; index++)
        {
            Assert.False(run.IsDecided);
            Assert.Null(run.Outcome);
            Assert.Equal(index, run.NextIndex);
            Assert.Equal(Commands[index], run.NextCommand);
            run = run.Record(Ended(index, 0));
        }

        Assert.True(run.IsDecided);
    }

    [Fact]
    public void A_command_that_exits_non_zero_ends_Validation_as_a_validation_failure_and_the_rest_are_recorded_as_not_run()
    {
        var run = Passing(1).Record(Ended(1, 1, seconds: 12));

        Assert.True(run.IsDecided);
        Assert.Equal(ValidationOutcome.ValidationFailure, run.Outcome);
        Assert.Equal(FailureClass.ValidationFailure, ValidationOutcome.ValidationFailure.Failure());
        Assert.Equal("validation-failure", ValidationOutcome.ValidationFailure.Tag());
        Assert.Null(run.NextCommand);

        var results = run.Results;
        Assert.Equal([ValidationCommandStatus.Passed, ValidationCommandStatus.Failed, ValidationCommandStatus.NotRun], results.Select(result => result.Status));
        Assert.Equal(1, results[1].ExitCode);
        Assert.Equal(TimeSpan.FromSeconds(12), results[1].Duration);
        Assert.Equal(Run(1), results[1].StepRunId);
        Assert.Equal(new ValidationCommandResult(2, "npm run lint", ValidationCommandStatus.NotRun, null, null, null), results[2]);
    }

    [Fact]
    public void Every_status_other_than_zero_fails_whatever_the_number()
    {
        foreach (var exitCode in new[] { 1, 2, 126, 127, 255, -1 })
        {
            Assert.Equal(ValidationOutcome.ValidationFailure, ValidationRun.Begin(Commands).Record(Ended(0, exitCode)).Outcome);
        }
    }

    [Fact]
    public void A_command_stopped_at_its_deadline_is_a_timeout_whatever_status_the_stop_gave_it()
    {
        foreach (var exitCode in new[] { 0, 137, 143 })
        {
            var run = Passing(1).Record(Ended(1, exitCode, StepExitCause.Timeout));

            Assert.Equal(ValidationOutcome.Timeout, run.Outcome);
            Assert.Equal(ValidationCommandStatus.TimedOut, run.Recorded[1].Status);
            Assert.Equal(exitCode, run.Recorded[1].ExitCode);
            Assert.Equal(ValidationCommandStatus.NotRun, run.Results[2].Status);
        }

        Assert.Equal(FailureClass.Timeout, ValidationOutcome.Timeout.Failure());
        Assert.Equal("timeout", ValidationOutcome.Timeout.Tag());
    }

    [Theory]
    [InlineData(StepExitCause.OutOfMemory, 137)]
    [InlineData(StepExitCause.Lost, null)]
    public void A_command_the_runtime_could_not_see_to_its_end_is_an_infrastructure_error_and_never_a_pass(StepExitCause cause, int? exitCode)
    {
        var result = ValidationCommandResult.Ended(0, Commands[0], Run(0), exitCode, cause, TimeSpan.FromSeconds(1));
        var run = ValidationRun.Begin(Commands).Record(result);

        Assert.Equal(ValidationCommandStatus.Interrupted, result.Status);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(ValidationOutcome.InfrastructureError, run.Outcome);
        Assert.Equal(FailureClass.InfrastructureError, ValidationOutcome.InfrastructureError.Failure());
        Assert.Equal("infrastructure-error", ValidationOutcome.InfrastructureError.Tag());
    }

    [Fact]
    public void A_command_that_ended_by_itself_with_no_exit_status_does_not_pass()
    {
        var result = ValidationCommandResult.Ended(0, Commands[0], Run(0), null, StepExitCause.Exited, TimeSpan.Zero);

        Assert.Equal(ValidationCommandStatus.Interrupted, result.Status);
    }

    [Fact]
    public void Only_a_result_of_the_next_command_is_taken()
    {
        var run = ValidationRun.Begin(Commands);

        Assert.Throws<ArgumentException>(() => run.Record(Ended(1, 0)));
        Assert.Throws<ArgumentException>(() => run.Record(ValidationCommandResult.Ended(0, "something else", Run(0), 0, StepExitCause.Exited, TimeSpan.Zero)));
        Assert.Throws<ArgumentException>(() => run.Record(ValidationCommandResult.NotRun(0, Commands[0])));
        Assert.Throws<ArgumentNullException>(() => run.Record(null!));
        Assert.Empty(run.Recorded);
    }

    [Fact]
    public void Nothing_is_recorded_after_Validation_is_decided()
    {
        var failed = Passing(0).Record(Ended(0, 2));

        Assert.Throws<InvalidOperationException>(() => failed.Record(Ended(1, 0)));
        Assert.Throws<InvalidOperationException>(() => Passing(3).Record(Ended(0, 0)));
    }

    [Fact]
    public void The_results_of_an_undecided_Validation_are_not_yet_known()
    {
        var run = Passing(1);

        Assert.Throws<InvalidOperationException>(() => run.Results);
        Assert.Throws<InvalidOperationException>(() => run.ToJson());
    }

    [Fact]
    public void Recording_leaves_the_earlier_run_as_it_was()
    {
        var before = ValidationRun.Begin(Commands);
        var after = before.Record(Ended(0, 0));

        Assert.Empty(before.Recorded);
        Assert.Single(after.Recorded);
        Assert.Equal(Commands, after.Commands);
    }

    [Fact]
    public void A_run_needs_at_least_one_command_and_none_may_be_blank()
    {
        Assert.Throws<ArgumentException>(() => ValidationRun.Begin([]));
        Assert.Throws<ArgumentException>(() => ValidationRun.Begin(["dotnet build", " "]));
        Assert.Throws<ArgumentNullException>(() => ValidationRun.Begin(null!));
    }

    [Fact]
    public void A_command_cannot_have_run_for_less_than_no_time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidationCommandResult.Ended(0, "x", Run(0), 0, StepExitCause.Exited, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void The_outcome_has_no_input_but_what_the_runtime_saw_of_the_containers()
    {
        // The two ways to reach a decision take a command's exit status and cause, and nothing an Agent returned: no result type of an Agent step is a parameter of either.
        var parameters = typeof(ValidationRun).GetMethods().Concat<System.Reflection.MethodBase>(typeof(ValidationCommandResult).GetMethods())
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType.Namespace)
            .Where(space => space is not null && space.StartsWith("Hephaestus.Core.Results", StringComparison.Ordinal));

        Assert.Empty(parameters);
    }

    [Fact]
    public void The_document_gives_the_outcome_and_for_each_command_its_status_exit_status_duration_and_output()
    {
        using var document = JsonDocument.Parse(Passing(1).Record(Ended(1, 3, seconds: 12)).ToJson());
        var root = document.RootElement;

        Assert.Equal("validation-failure", root.GetProperty("outcome").GetString());
        var commands = root.GetProperty("commands").EnumerateArray().ToList();
        Assert.Equal(3, commands.Count);
        Assert.Equal(["passed", "failed", "not-run"], commands.Select(command => command.GetProperty("status").GetString()));
        Assert.Equal(["dotnet build", "dotnet test", "npm run lint"], commands.Select(command => command.GetProperty("command").GetString()));
        Assert.Equal([0, 1, 2], commands.Select(command => command.GetProperty("index").GetInt32()));
        Assert.Equal(3, commands[1].GetProperty("exitCode").GetInt32());
        Assert.Equal(12_000, commands[1].GetProperty("durationMilliseconds").GetInt64());
        Assert.Equal(Run(1), commands[1].GetProperty("output").GetGuid());
        Assert.Equal(JsonValueKind.Null, commands[2].GetProperty("exitCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, commands[2].GetProperty("durationMilliseconds").ValueKind);
        Assert.Equal(JsonValueKind.Null, commands[2].GetProperty("output").ValueKind);
    }

    [Fact]
    public void The_same_results_are_the_same_bytes()
    {
        Assert.Equal(Passing(3).ToJson(), Passing(3).ToJson());
        Assert.Equal("passed", JsonDocument.Parse(Passing(3).ToJson()).RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task The_recorded_results_are_the_validation_file_of_a_later_steps_context()
    {
        var json = Passing(1).Record(Ended(1, 1)).ToJson();
        var template = new Methodology("default", [new WorkspaceFile("manifest.yml", "name: default\n"u8.ToArray()), new WorkspaceFile("correction.md", "Read /hephaestus/context/validation.json.\n"u8.ToArray())]);
        var input = new StepContextInput(
            Guid.NewGuid(), Guid.NewGuid(), 101, 7, "hephaestus/7-add-a-thing", "0123456789abcdef0123456789abcdef01234567", "main", StepKind.Correction, 1, Hephaestus.Core.Localization.Language.English)
        {
            Validation = json,
        };

        var assembled = await StepContextAssembler.AssembleAsync(new SingleCatalog(template), input, TestContext.Current.CancellationToken);

        Assert.Equal(json, System.Text.Encoding.UTF8.GetString(assembled.Context.Single(file => file.Path == ContextFiles.Validation).Content));
    }

    [Theory]
    [InlineData(null, null, 1800)]
    [InlineData(7200, null, 1800)]
    [InlineData(600, null, 600)]
    [InlineData(null, 900, 900)]
    [InlineData(null, 7200, 1800)]
    [InlineData(600, 900, 600)]
    [InlineData(1200, 900, 900)]
    [InlineData(7200, 900, 900)]
    public void A_command_has_a_limit_the_Task_duration_caps_and_the_installations_caps_the_Projects(int? project, int? installation, int expectedSeconds)
    {
        var limit = ValidationRun.CommandLimit(
            new ProjectLimits(project is { } p ? TimeSpan.FromSeconds(p) : null, null, null),
            new InstallationLimits(installation is { } i ? TimeSpan.FromSeconds(i) : null, null, null));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), limit);
    }

    private sealed class SingleCatalog(Methodology methodology) : IMethodologyCatalog
    {
        public Task<Methodology?> ReadAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Methodology?>(methodology);

        public IReadOnlyList<string> ListNames() => [methodology.Name];
    }
}
