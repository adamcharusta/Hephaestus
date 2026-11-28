using Hephaestus.Core.Effects;
using Hephaestus.Core.Identity;
using Hephaestus.Core.PullRequests;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Results;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>
/// Story 3.13, AD-1, AD-5, FR-36: the intent of a publication is journaled with the facts it is composed from and is found again with them after a restart; its result moves the Task to <c>pull-request-open</c> in the same transaction, with the
/// intent of the state label, and only while the Task is still where the publication found it.
/// </summary>
public class PullRequestJournalTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly FixedTimeProvider Clock = new(Noon);
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PullRequestFacts Facts() =>
        PullRequestFacts.From(
            "Add a login page",
            new ImplementationResult(StepOutcome.Completed, "Adds a login page.", []),
            PullRequestAgent.For(Agent.ClaudeCode, "claude-opus-5-5", Effort.High),
            ValidationRun.Begin(["dotnet test"]).Record(ValidationCommandResult.Ended(0, "dotnet test", Guid.CreateVersion7(), 0, StepExitCause.Exited, TimeSpan.FromSeconds(3))));

    private static async Task<(TempInstallation Installation, TaskRecord Task, Effect Effect)> StartAsync(TaskState state = TaskState.Running)
    {
        var installation = new TempInstallation(Clock);
        await installation.MigrateAsync();
        var task = new TaskRecord(EntityId.New(Clock), 101, 7, state, "hephaestus/7-add-a-login-page", Noon, Noon);
        var snapshot = new IssueSnapshot("Add a login page", "Details.", IssueContent.Hash("Add a login page", "Details."), 1, Noon);
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync(
            [new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, 101, 7, 29, "alice", "hephaestus", null, null, Noon, "Add a login page", "Details.")],
            Token);
        var intent = new PullRequestIntent(StepRunId, new string('a', 40));
        await installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(
            new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, snapshot), NewEffects: [intent.ToNewEffect(task.Id, Facts())]),
            Token);
        var pending = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token));
        return (installation, task, pending.Effect);
    }

    private static TaskMove Move(Guid taskId) => new(TaskState.Running, TaskState.PullRequestOpen, [new NewEffect(taskId, EffectKeys.StepCause(StepRunId), EffectKind.Label, "pull-request-open")]);

    [Fact]
    public async Task The_intent_is_journaled_with_the_facts_and_found_again_unfinished_with_them()
    {
        var (installation, task, effect) = await StartAsync();
        using var _ = installation;

        Assert.Equal($"{task.Id}:{EffectKeys.StepCause(StepRunId)}:pull-request:0", effect.Key);
        Assert.Equal(EffectKind.PullRequest, effect.Kind);
        Assert.True(PullRequestIntent.TryParse(effect.Detail, out var intent));
        Assert.Equal(StepRunId, intent.StepRunId);
        Assert.True(PullRequestFacts.TryParse(effect.Payload, out var facts));
        Assert.Equal(Facts().Checks, facts.Checks);
        Assert.Equal("Add a login page", facts.Title);
        var listed = Assert.Single(await installation.Reads.ListJournalAsync(task.Id, Token));
        Assert.Equal(effect.Payload, listed.Effect.Payload);
    }

    [Fact]
    public async Task An_intent_that_has_no_facts_has_no_payload()
    {
        using var installation = new TempInstallation(Clock);
        await installation.MigrateAsync();
        var task = new TaskRecord(EntityId.New(Clock), 101, 7, TaskState.Queued, "hephaestus/7-add-a-login-page", Noon, Noon);
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync(
            [new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, 101, 7, 29, "alice", "hephaestus", null, null, Noon, "Add a login page", "Details.")],
            Token);
        await installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(
            new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, new IssueSnapshot("t", "b", IssueContent.Hash("t", "b"), 1, Noon)), NewEffects: [new NewEffect(task.Id, "g1", EffectKind.Label, "queued")]),
            Token);

        Assert.Null(Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token)).Effect.Payload);
    }

    [Fact]
    public async Task The_result_moves_the_Task_to_pull_request_open_and_journals_the_intent_of_its_label_in_the_same_transaction()
    {
        var (installation, task, effect) = await StartAsync();
        using var _ = installation;

        Assert.True(await installation.Provider.GetRequiredService<IUnitOfWork>().RecordEffectAsync(new EffectRecording(effect, EffectResult.Applied, null, 0, null, Move: Move(task.Id)), Token));

        Assert.Equal(TaskState.PullRequestOpen, (await installation.Reads.FindTaskAsync(101, 7, Token))!.State);
        var label = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token));
        Assert.Equal(($"{task.Id}:{EffectKeys.StepCause(StepRunId)}:label:0", EffectKind.Label, "pull-request-open"), (label.Effect.Key, label.Effect.Kind, label.Effect.Detail));
        Assert.Equal(TaskState.PullRequestOpen, label.Task!.State);
        Assert.Equal(EffectResult.Applied, (await installation.Reads.ListJournalAsync(task.Id, Token)).Single(e => e.Entry == JournalEntryKind.Result).Result);
    }

    [Theory]
    [InlineData(TaskState.Cancelled)]
    [InlineData(TaskState.WaitingForOwner)]
    [InlineData(TaskState.PullRequestOpen)]
    public async Task A_Task_that_is_no_longer_where_the_publication_found_it_is_left_as_it_is_and_no_intent_is_journaled_for_the_move(TaskState state)
    {
        var (installation, task, effect) = await StartAsync(state);
        using var _ = installation;

        Assert.True(await installation.Provider.GetRequiredService<IUnitOfWork>().RecordEffectAsync(new EffectRecording(effect, EffectResult.Applied, null, 0, null, Move: Move(task.Id)), Token));

        Assert.Equal(state, (await installation.Reads.FindTaskAsync(101, 7, Token))!.State);
        Assert.Empty(await installation.Reads.ListUnfinishedEffectsAsync(Token));
        Assert.Single(await installation.Reads.ListJournalAsync(task.Id, Token), e => e.Entry == JournalEntryKind.Result);
    }

    [Fact]
    public async Task A_result_recorded_a_second_time_moves_nothing_and_journals_nothing()
    {
        var (installation, task, effect) = await StartAsync();
        using var _ = installation;
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await unit.RecordEffectAsync(new EffectRecording(effect, EffectResult.Applied, null, 0, null, Move: Move(task.Id)), Token);
        var journal = (await installation.Reads.ListJournalAsync(task.Id, Token)).Count;

        Assert.False(await unit.RecordEffectAsync(new EffectRecording(effect, EffectResult.Applied, null, 0, null, Move: Move(task.Id)), Token));

        Assert.Equal(journal, (await installation.Reads.ListJournalAsync(task.Id, Token)).Count);
    }

    [Fact]
    public async Task A_retry_with_no_move_leaves_the_Task_where_it_is()
    {
        var (installation, task, effect) = await StartAsync();
        using var _ = installation;

        await installation.Provider.GetRequiredService<IUnitOfWork>().RecordEffectAsync(new EffectRecording(effect, null, "unreachable", 1, Noon.AddMinutes(1)), Token);

        Assert.Equal(TaskState.Running, (await installation.Reads.FindTaskAsync(101, 7, Token))!.State);
        Assert.Equal(task.Id, Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token)).Task!.Id);
    }
}
