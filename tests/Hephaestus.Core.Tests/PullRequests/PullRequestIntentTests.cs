using Hephaestus.Core.Effects;
using Hephaestus.Core.PullRequests;
using static Hephaestus.Core.Tests.PullRequests.PullRequestFixtures;

namespace Hephaestus.Core.Tests.PullRequests;

/// <summary>Story 3.13, AD-5, FR-36: the intent of a publication names the step run and the tip, carries the facts, and is an effect of its own.</summary>
public class PullRequestIntentTests
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");

    [Fact]
    public void The_detail_names_the_step_run_and_the_tip_and_reads_back()
    {
        Assert.Equal($"pull-request|{StepRunId}|{Tip}", Intent.Detail);
        Assert.True(Intent.Detail.Length <= 256);
        Assert.True(PullRequestIntent.TryParse(Intent.Detail, out var read));
        Assert.Equal(Intent, read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("push|018f4b6e-0000-7000-8000-000000000002|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("pull-request|not-a-guid|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("pull-request|00000000-0000-0000-0000-000000000000|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("pull-request|018f4b6e-0000-7000-8000-000000000002|AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("pull-request|018f4b6e-0000-7000-8000-000000000002|abc")]
    public void A_detail_this_version_did_not_write_is_not_read(string? detail)
    {
        Assert.False(PullRequestIntent.TryParse(detail, out _));
    }

    [Fact]
    public void The_effect_to_journal_is_a_pull_request_of_the_Task_whose_cause_is_the_step_run_and_whose_payload_is_the_facts()
    {
        var facts = Facts();

        var effect = Intent.ToNewEffect(TaskId, facts);

        Assert.Equal((TaskId, EffectKind.PullRequest, "s018f4b6e00007000800000000000000"), (effect.TaskId, effect.Kind, effect.Cause));
        Assert.True(effect.Cause.Length <= 32);
        Assert.Equal(Intent.Detail, effect.Detail);
        Assert.True(PullRequestFacts.TryParse(effect.Payload, out var read));
        Assert.Equal(facts.Title, read.Title);
        Assert.Equal(facts.Checks, read.Checks);
        Assert.True(EffectKeys.For(TaskId, effect.Cause, EffectKind.PullRequest, 0).Length <= 128);
    }

    [Fact]
    public void The_kind_has_a_name_that_reads_back_and_a_publication_without_facts_is_a_mistake()
    {
        Assert.Equal("pull-request", EffectKind.PullRequest.Tag());
        Assert.True(EffectKinds.TryParse("pull-request", out var kind));
        Assert.Equal(EffectKind.PullRequest, kind);
        Assert.Throws<ArgumentNullException>(() => Intent.ToNewEffect(TaskId, null!));
    }
}
