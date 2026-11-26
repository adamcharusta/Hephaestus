using Hephaestus.Core.Aliases;

namespace Hephaestus.Core.Tests.Aliases;

/// <summary>Story 1.9, FR-7, AD-10: an Alias names an Agent and a Model, or an Effort of Core's closed set; nothing else is stored.</summary>
public class AliasServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (AliasService Service, FakeInstallation Store) Create()
    {
        var store = new FakeInstallation();
        return (new AliasService(store, new FixedTime(Noon)), store);
    }

    [Theory]
    [InlineData("claude-code")]
    [InlineData("codex-cli")]
    [InlineData(" Codex-CLI ")]
    public async Task An_Agent_and_a_Model_make_an_Alias_for_either_Agent(string agent)
    {
        var (service, store) = Create();

        var result = await service.AddAsync("opus5.5", agent, "claude-opus-5-5", null, Token);

        Assert.Equal(AliasFailure.None, result.Failure);
        Assert.True(result.Changed);
        var alias = Assert.Single(store.Aliases.Values);
        Assert.Equal("opus5.5", alias.Name);
        Assert.Equal(Agents.TryParse(agent, out var parsed) ? parsed : throw new InvalidOperationException(), alias.Agent);
        Assert.Equal("claude-opus-5-5", alias.Model);
        Assert.Null(alias.Effort);
        Assert.Equal(Noon, alias.AddedAt);
    }

    [Theory]
    [InlineData("gemini")]
    [InlineData("claude")]
    [InlineData("")]
    public async Task An_Agent_that_is_neither_Claude_Code_nor_Codex_CLI_is_refused_and_nothing_is_stored(string agent)
    {
        var (service, store) = Create();

        var result = await service.AddAsync("x", agent, "some-model", null, Token);

        Assert.Equal(AliasFailure.UnknownAgent, result.Failure);
        Assert.Empty(store.Aliases);
    }

    [Theory]
    [InlineData("low", Effort.Low)]
    [InlineData("medium", Effort.Medium)]
    [InlineData("high", Effort.High)]
    [InlineData("xhigh", Effort.ExtraHigh)]
    [InlineData("MAX", Effort.Max)]
    public async Task Every_value_of_the_closed_Effort_set_makes_an_Effort_Alias(string effort, Effort expected)
    {
        var (service, store) = Create();

        var result = await service.AddAsync("deep", null, null, effort, Token);

        Assert.Equal(AliasFailure.None, result.Failure);
        var alias = Assert.Single(store.Aliases.Values);
        Assert.Equal(expected, alias.Effort);
        Assert.Null(alias.Agent);
        Assert.Null(alias.Model);
    }

    [Theory]
    [InlineData("ultra")]
    [InlineData("none")]
    [InlineData("")]
    public async Task An_Effort_outside_the_closed_set_is_refused(string effort)
    {
        var (service, store) = Create();

        var result = await service.AddAsync("deep", null, null, effort, Token);

        Assert.Equal(AliasFailure.UnknownEffort, result.Failure);
        Assert.Empty(store.Aliases);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("claude-code", null, null)]
    [InlineData(null, "model", null)]
    [InlineData("claude-code", "model", "high")]
    [InlineData(null, "model", "high")]
    [InlineData("claude-code", null, "high")]
    public async Task An_Alias_names_an_Agent_with_a_Model_or_an_Effort_and_nothing_between(string? agent, string? model, string? effort)
    {
        var (service, store) = Create();

        var result = await service.AddAsync("x", agent, model, effort, Token);

        Assert.Equal(AliasFailure.Incomplete, result.Failure);
        Assert.Empty(store.Aliases);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Opus")]
    [InlineData("-opus")]
    [InlineData("two words")]
    [InlineData("label:name")]
    [InlineData("opus\n")]
    public async Task A_name_a_label_cannot_carry_is_refused(string name)
    {
        var (service, store) = Create();

        var result = await service.AddAsync(name, "claude-code", "model", null, Token);

        Assert.Equal(AliasFailure.InvalidName, result.Failure);
        Assert.Empty(store.Aliases);
    }

    [Fact]
    public async Task The_longest_name_is_accepted_and_one_more_character_is_not()
    {
        var (service, _) = Create();

        Assert.Equal(AliasFailure.None, (await service.AddAsync(new string('a', AliasShape.MaximumNameLength), null, null, "low", Token)).Failure);
        Assert.Equal(AliasFailure.InvalidName, (await service.AddAsync(new string('a', AliasShape.MaximumNameLength + 1), null, null, "low", Token)).Failure);
    }

    [Theory]
    [InlineData("-model")]
    [InlineData("a model")]
    [InlineData("model;rm")]
    [InlineData("model\n")]
    [InlineData("")]
    public async Task A_Model_that_could_be_taken_for_an_option_or_is_not_a_name_is_refused(string model)
    {
        var (service, store) = Create();

        var result = await service.AddAsync("x", "claude-code", model, null, Token);

        Assert.Equal(AliasFailure.InvalidModel, result.Failure);
        Assert.Empty(store.Aliases);
    }

    [Theory]
    [InlineData("claude-opus-5-5")]
    [InlineData("gpt-5.5-codex")]
    [InlineData("anthropic/claude-sonnet-5-5@20261101")]
    [InlineData("llama3:70b")]
    public async Task Any_Model_a_vendor_names_is_accepted_so_a_new_one_needs_no_change_to_Hephaestus(string model)
    {
        var (service, store) = Create();

        var result = await service.AddAsync("m", "codex-cli", model, null, Token);

        Assert.Equal(AliasFailure.None, result.Failure);
        Assert.Equal(model, store.Aliases["m"].Model);
    }

    [Fact]
    public async Task Defining_the_same_Alias_again_changes_nothing_and_says_so()
    {
        var (service, store) = Create();
        await service.AddAsync("opus", "claude-code", "claude-opus-5-5", null, Token);

        var again = await service.AddAsync("opus", "claude-code", "claude-opus-5-5", null, Token);

        Assert.Equal(AliasFailure.None, again.Failure);
        Assert.False(again.Changed);
        Assert.Single(store.Aliases);
    }

    [Fact]
    public async Task Defining_a_name_that_means_something_else_is_refused_and_the_Alias_stays_as_it_was()
    {
        var (service, store) = Create();
        await service.AddAsync("opus", "claude-code", "claude-opus-5-5", null, Token);

        var other = await service.AddAsync("opus", "codex-cli", "gpt-5.5", null, Token);
        var effort = await service.AddAsync("opus", null, null, "high", Token);

        Assert.Equal(AliasFailure.DefinedDifferently, other.Failure);
        Assert.Equal(AliasFailure.DefinedDifferently, effort.Failure);
        Assert.Equal("claude-opus-5-5", store.Aliases["opus"].Model);
    }

    [Fact]
    public async Task Removing_an_Alias_returns_it_and_a_second_removal_finds_nothing()
    {
        var (service, store) = Create();
        await service.AddAsync("opus", "claude-code", "claude-opus-5-5", null, Token);

        var removed = await service.RemoveAsync("opus", Token);
        var again = await service.RemoveAsync("opus", Token);
        var malformed = await service.RemoveAsync("Not A Name", Token);

        Assert.Equal("claude-opus-5-5", removed.Alias?.Model);
        Assert.Equal(AliasFailure.NotDefined, again.Failure);
        Assert.Equal(AliasFailure.NotDefined, malformed.Failure);
        Assert.Empty(store.Aliases);
    }

    [Fact]
    public void The_Agent_and_Effort_tags_are_the_closed_sets_in_order()
    {
        Assert.Equal(["claude-code", "codex-cli"], Agents.Tags);
        Assert.Equal(["low", "medium", "high", "xhigh", "max"], Efforts.Tags);
        Assert.False(Agents.TryParse(null, out _));
        Assert.False(Efforts.TryParse(null, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((Agent)99).Tag());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((Effort)99).Tag());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
