using Hephaestus.Core.Effects;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Effects;

/// <summary>AD-5, AD-9, FR-20, FR-30, Story 3.7: the journal's intent of a push names the step run, the checksum, and the tips before and after, and fits the journal's detail.</summary>
public class PushIntentTests
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
    private static readonly BundleReference Bundle = new(BundleReference.CheckpointPath(TaskId, StepRunId), new string('d', 64), new string('a', 40), new string('b', 40));
    private static readonly string Start = new('c', 40);

    [Fact]
    public void The_detail_names_the_step_run_the_checksum_and_the_tips_and_reads_back()
    {
        var intent = PushIntent.For(StepRunId, Bundle, Start);

        Assert.Equal($"push|{StepRunId}|{new string('d', 64)}|{new string('a', 40)}|{new string('b', 40)}|{Start}", intent.Detail);
        Assert.True(intent.Detail.Length <= 256);
        Assert.True(PushIntent.TryParse(intent.Detail, out var read));
        Assert.Equal(intent, read);
        Assert.Equal(Bundle, intent.Bundle(TaskId));
    }

    [Fact]
    public void The_effect_to_journal_is_a_push_of_the_Task_whose_cause_is_the_step_run_and_fits_a_cause()
    {
        var effect = PushIntent.For(StepRunId, Bundle, Start).ToNewEffect(TaskId);

        Assert.Equal(EffectKind.Push, effect.Kind);
        Assert.Equal(TaskId, effect.TaskId);
        Assert.Equal("s018f4b6e00007000800000000000000", effect.Cause);
        Assert.True(effect.Cause.Length <= 32);
        Assert.Equal($"{TaskId}:{effect.Cause}:push:0", EffectKeys.For(TaskId, effect.Cause, EffectKind.Push, 0));
        Assert.True(EffectKeys.For(TaskId, effect.Cause, EffectKind.Push, 0).Length <= 128);
    }

    [Fact]
    public void The_kind_has_a_name_that_reads_back()
    {
        Assert.Equal("push", EffectKind.Push.Tag());
        Assert.True(EffectKinds.TryParse("push", out var kind));
        Assert.Equal(EffectKind.Push, kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("push")]
    [InlineData("label|queued")]
    [InlineData("push|not-a-guid|x|y|z|w")]
    [InlineData("push|00000000-0000-0000-0000-000000000000|dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb|cccccccccccccccccccccccccccccccccccccccc")]
    public void A_detail_this_version_did_not_write_is_not_read(string? detail)
    {
        Assert.False(PushIntent.TryParse(detail, out _));
    }

    [Fact]
    public void A_detail_with_an_upper_case_digit_or_a_short_commit_is_not_read()
    {
        var good = PushIntent.For(StepRunId, Bundle, Start).Detail;

        Assert.False(PushIntent.TryParse(good.Replace('a', 'A'), out _));
        Assert.False(PushIntent.TryParse(good[..^1], out _));
        Assert.False(PushIntent.TryParse(good + "|x", out _));
    }

    [Fact]
    public void A_bundle_with_no_prerequisite_has_no_tip_before()
    {
        Assert.Throws<ArgumentException>(() => PushIntent.For(StepRunId, new BundleReference(Bundle.Path, Bundle.Sha256, Bundle.Tip), Start));
        Assert.Throws<ArgumentNullException>(() => PushIntent.For(StepRunId, null!, Start));
    }

    [Fact]
    public void The_path_the_runtime_writes_the_checkpoint_under_is_the_one_the_intent_reads_it_by()
    {
        Assert.Equal($"{TaskId}/{StepRunId}-checkpoint.bundle", BundleReference.CheckpointPath(TaskId, StepRunId));
    }
}
