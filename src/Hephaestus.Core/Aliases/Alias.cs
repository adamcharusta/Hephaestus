using System.Text.RegularExpressions;

namespace Hephaestus.Core.Aliases;

/// <summary>
/// A name the Owner defined that resolves to an Agent with a Model, or to an Effort (FR-7). Exactly one of the two: an Alias with an
/// Agent has a Model and no Effort, and the other way round. A Role Label carries the name (<c>primary:opus5.5</c>).
/// </summary>
public sealed record Alias
{
    private Alias(string name, Agent? agent, string? model, Effort? effort, DateTimeOffset addedAt)
    {
        Name = name;
        Agent = agent;
        Model = model;
        Effort = effort;
        AddedAt = addedAt;
    }

    public string Name { get; }

    /// <summary>The Agent, for an Alias that names an Agent and a Model; otherwise null.</summary>
    public Agent? Agent { get; }

    /// <summary>The Model, opaque to Hephaestus: a new one needs no change to it. Null for an Effort Alias.</summary>
    public string? Model { get; }

    /// <summary>The Effort, for an Effort Alias; otherwise null.</summary>
    public Effort? Effort { get; }

    public DateTimeOffset AddedAt { get; }

    public static Alias ForModel(string name, Agent agent, string model, DateTimeOffset addedAt) => new(name, agent, model, null, addedAt);

    public static Alias ForEffort(string name, Effort effort, DateTimeOffset addedAt) => new(name, null, null, effort, addedAt);

    /// <summary>Whether two Aliases define the same thing, whenever each was added.</summary>
    public bool Defines(Alias other) => Name == other.Name && Agent == other.Agent && Model == other.Model && Effort == other.Effort;
}

/// <summary>The shapes of an Alias's name and Model.</summary>
public static partial class AliasShape
{
    public const int MaximumNameLength = 40;
    public const int MaximumModelLength = 100;

    /// <summary>
    /// Lower case letters, digits, <c>.</c>, <c>_</c>, and <c>-</c>, starting with a letter or digit: a name a GitHub label can carry
    /// after <c>primary:</c> or <c>fallback:</c> within the label's 50 characters.
    /// </summary>
    public static bool IsValidName(string? name) => name is { Length: > 0 and <= MaximumNameLength } && NameShape().IsMatch(name);

    /// <summary>
    /// A Model's name as a vendor writes it: letters, digits, and <c>. _ : / @ -</c>, starting with a letter or digit, so that it can
    /// never be taken for an option when it is passed to a CLI.
    /// </summary>
    public static bool IsValidModel(string? model) => model is { Length: > 0 and <= MaximumModelLength } && ModelShape().IsMatch(model);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NameShape();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:/@-]*\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ModelShape();
}
