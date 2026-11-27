using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>The Task branches on GitHub as the loop asks them, in memory: what a push answers, what it was asked, and which bundles were deleted and what the journal held then.</summary>
internal sealed class FakeBranches(LoopStores stores) : ITaskBranches
{
    /// <summary>What a push answers, in order, before it answers <see cref="PushResult.Pushed"/>.</summary>
    public Queue<PushResult> Answers { get; } = [];

    /// <summary>When set, a push throws it.</summary>
    public Exception? Throws { get; set; }

    public List<(PushRequest Request, string Key)> Pushes { get; } = [];

    /// <summary>The bundles deleted, with the number of journal lines there were at the time, so that a test can say the result was written first.</summary>
    public List<(BundleReference Bundle, int JournalLines, bool HadResult)> Deleted { get; } = [];

    public Task<BranchCheckResult> CheckBranchAsync(BranchCheckRequest request, CancellationToken cancellationToken) => Task.FromResult(BranchCheckResult.InSync());

    public Task<PushResult> PushAsync(PushRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        Pushes.Add((request, idempotencyKey));
        return Throws is not null ? Task.FromException<PushResult>(Throws) : Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : PushResult.Pushed());
    }

    public void DeleteBundle(BundleReference bundle) =>
        Deleted.Add((bundle, stores.Journal.Count, stores.Journal.Any(e => e.Entry == Hephaestus.Core.Effects.JournalEntryKind.Result && e.Effect.Kind == Hephaestus.Core.Effects.EffectKind.Push)));
}
