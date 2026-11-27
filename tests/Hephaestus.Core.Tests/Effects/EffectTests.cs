using Hephaestus.Core.Effects;
using Hephaestus.Core.Settings;

namespace Hephaestus.Core.Tests.Effects;

/// <summary>Story 2.4, AD-5: the key of an effect, the names the journal stores, and the bounds of a retry.</summary>
public class EffectTests
{
    private static readonly Guid Task = Guid.Parse("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b");

    [Fact]
    public void The_key_is_scope_cause_effect_kind_and_ordinal_and_a_signal_cause_is_g_and_its_sequence()
    {
        Assert.Equal("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g12:label:0", EffectKeys.For(Task, EffectKeys.SignalCause(12), EffectKind.Label, 0));
        Assert.Equal("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g12:label:3", EffectKeys.For(Task, "g12", EffectKind.Label, 3));
    }

    [Fact]
    public void Every_kind_result_and_entry_has_a_name_that_reads_back_and_a_value_outside_the_enumeration_has_none()
    {
        foreach (var kind in Enum.GetValues<EffectKind>())
        {
            Assert.True(EffectKinds.TryParse(kind.Tag(), out var parsed));
            Assert.Equal(kind, parsed);
        }

        foreach (var result in Enum.GetValues<EffectResult>())
        {
            Assert.True(EffectResults.TryParse(result.Tag(), out var parsed));
            Assert.Equal(result, parsed);
        }

        foreach (var entry in Enum.GetValues<JournalEntryKind>())
        {
            Assert.True(JournalEntryKinds.TryParse(entry.Tag(), out var parsed));
            Assert.Equal(entry, parsed);
        }

        Assert.Equal(["applied", "already-in-place", "superseded", "gave-up", "refused"], Enum.GetValues<EffectResult>().Select(EffectResults.Tag));
        Assert.Equal(["intent", "retry", "result"], Enum.GetValues<JournalEntryKind>().Select(JournalEntryKinds.Tag));
        Assert.False(EffectKinds.TryParse("push", out _));
        Assert.False(EffectResults.TryParse(null, out _));
        Assert.False(JournalEntryKinds.TryParse("", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((EffectKind)9).Tag());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((EffectResult)9).Tag());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((JournalEntryKind)9).Tag());
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    [InlineData(6, 600)]
    [InlineData(40, 600)]
    public void The_wait_after_a_failure_doubles_from_thirty_seconds_and_stops_at_ten_minutes(int failures, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), EffectRetries.DelayAfter(failures));
    }

    [Fact]
    public void A_new_installation_retries_an_effect_five_times()
    {
        Assert.Equal(5, InstallationSettings.EffectRetriesFrom(new Dictionary<string, string>()));
        Assert.Equal("0-20", InstallationSettings.EffectRetries.Accepts);
        Assert.Equal("5", InstallationSettings.EffectRetries.Default);
        Assert.Same(InstallationSettings.EffectRetries, InstallationSettings.Find("effectRetries"));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("7", "7")]
    [InlineData("20", "20")]
    public void The_bound_of_retries_is_stored_as_a_whole_number_from_zero_to_twenty_and_read_back(string value, string stored)
    {
        Assert.Equal(stored, InstallationSettings.EffectRetries.Accept(value));
        Assert.Equal(int.Parse(stored, System.Globalization.CultureInfo.InvariantCulture), InstallationSettings.EffectRetriesFrom(new Dictionary<string, string> { ["effectRetries"] = value }));
    }

    [Theory]
    [InlineData("21")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("5x")]
    [InlineData("")]
    [InlineData("٥")]
    [InlineData("100")]
    public void A_bound_outside_zero_to_twenty_or_not_a_whole_number_is_refused_and_a_stored_one_that_is_no_longer_valid_reads_as_the_default(string value)
    {
        Assert.Null(InstallationSettings.EffectRetries.Accept(value));
        Assert.Equal(5, InstallationSettings.EffectRetriesFrom(new Dictionary<string, string> { ["effectRetries"] = value }));
    }

    [Fact]
    public void The_scope_of_an_effect_on_an_issue_that_is_no_Task_is_the_Project_and_a_Tasks_is_its_id()
    {
        Assert.Equal("project:101:g4:comment:0", EffectKeys.For(EffectKeys.Scope(null, 101), "g4", EffectKind.Comment, 0));
        Assert.Equal("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g4:comment:1", EffectKeys.For(EffectKeys.Scope(Task, 101), "g4", EffectKind.Comment, 1));
        Assert.Equal("project:101", new NewEffect(null, "g4", EffectKind.Comment, "x", RepositoryId: 101, IssueNumber: 7).Scope);
        Assert.Throws<ArgumentException>(() => EffectKeys.Scope(null, null));
    }

    [Fact]
    public void The_marker_is_a_hidden_comment_with_the_key_and_is_the_last_line_after_one_empty_line()
    {
        Assert.Equal("<!-- hephaestus:effect=project:101:g4:comment:0 -->", EffectMarkers.For("project:101:g4:comment:0"));
        Assert.Equal("Said.\n\n<!-- hephaestus:effect=k -->", EffectMarkers.Append("Said.\n\n", "k"));
    }

    [Theory]
    [InlineData("Said.\n\n<!-- hephaestus:effect=k -->", true)]
    [InlineData("Said.\r\n\r\n<!-- hephaestus:effect=k -->\r\n", true)]
    [InlineData("<!-- hephaestus:effect=k -->", true)]
    [InlineData("<!-- hephaestus:effect=k -->\nMore.", false)]
    [InlineData("Said <!-- hephaestus:effect=k -->", false)]
    [InlineData("Said.\n\n<!-- hephaestus:effect=other -->", false)]
    [InlineData("<!-- hephaestus:effect=k-->", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_marker_counts_only_as_the_last_line_and_only_for_its_own_key(string? body, bool expected)
    {
        Assert.Equal(expected, EffectMarkers.EndsWith(body, "k"));
    }

    [Fact]
    public void The_comment_that_says_a_label_was_ignored_keeps_its_facts_in_the_detail_and_one_notice_for_each_user_kind_of_action_and_issue()
    {
        var notice = IssueNotices.IgnoredLabel(null, "g4", 101, 7, 1001, "bob", "hephaestus")!;

        Assert.Equal((EffectKind.Comment, "ignored-label|bob|hephaestus", "ignored-label:1001", 101L, 7), (notice.Kind, notice.Detail, notice.NoticeKey, notice.RepositoryId, notice.IssueNumber));
        Assert.True(IssueNotices.TryWrite(notice.Detail, Hephaestus.Core.Localization.Language.English, out var text));
        Assert.Equal("**Ignored.** `bob` is not a Trusted User, so the `hephaestus` label has no effect.", text);
        Assert.NotEqual(notice.NoticeKey, IssueNotices.IgnoredLabel(null, "g5", 101, 7, 1002, "carol", "hephaestus")!.NoticeKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("pipe|login")]
    [InlineData("`code`")]
    [InlineData("@everyone")]
    public void Nothing_but_a_login_is_put_in_a_comment_as_one(string? login)
    {
        Assert.Null(IssueNotices.IgnoredLabel(null, "g4", 101, 7, 1001, login, "hephaestus"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("something-else|x")]
    [InlineData("ignored-label|bob")]
    [InlineData("ignored-label|bob|hephaestus|extra")]
    public void A_detail_of_no_known_kind_is_not_written(string detail)
    {
        Assert.False(IssueNotices.TryWrite(detail, Hephaestus.Core.Localization.Language.English, out var text));
        Assert.Empty(text);
    }
}
