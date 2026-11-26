using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Aliases;

/// <summary>Why a change to the Aliases did not happen.</summary>
public enum AliasFailure
{
    /// <summary>It happened.</summary>
    None,

    /// <summary>The name is not label-safe (<see cref="AliasShape.IsValidName"/>).</summary>
    InvalidName,

    /// <summary>The Alias does not name exactly one of "an Agent and a Model" and "an Effort".</summary>
    Incomplete,

    /// <summary>The Agent is neither Claude Code nor Codex CLI.</summary>
    UnknownAgent,

    /// <summary>The Model is not shaped like a Model's name.</summary>
    InvalidModel,

    /// <summary>The Effort is outside Core's closed set (AD-10).</summary>
    UnknownEffort,

    /// <summary>The name is already an Alias that defines something else; it must be removed first.</summary>
    DefinedDifferently,

    /// <summary>The name is not an Alias.</summary>
    NotDefined,
}

/// <summary>The outcome of adding or removing an Alias.</summary>
/// <param name="Alias">The Alias that was added, found already defined, or removed.</param>
/// <param name="Changed">False when <c>add</c> found the same Alias already defined.</param>
public sealed record AliasResult(AliasFailure Failure, Alias? Alias, bool Changed)
{
    public static AliasResult Failed(AliasFailure failure) => new(failure, null, Changed: false);

    public static AliasResult Done(Alias alias, bool changed) => new(AliasFailure.None, alias, changed);
}

/// <summary>
/// The rules for Aliases (FR-7, AD-10): an Alias resolves to an Agent with a Model, or to an Effort of Core's closed set. A Model is
/// only checked for shape, so a new Model needs no change to Hephaestus. An Alias is never silently redefined.
/// </summary>
public sealed class AliasService(IInstallationConfiguration configuration, TimeProvider time)
{
    public async Task<AliasResult> AddAsync(string name, string? agent, string? model, string? effort, CancellationToken cancellationToken)
    {
        if (!AliasShape.IsValidName(name))
        {
            return AliasResult.Failed(AliasFailure.InvalidName);
        }

        var addedAt = time.GetUtcNow();
        Alias alias;
        if (effort is not null)
        {
            if (agent is not null || model is not null)
            {
                return AliasResult.Failed(AliasFailure.Incomplete);
            }

            if (!Efforts.TryParse(effort, out var parsedEffort))
            {
                return AliasResult.Failed(AliasFailure.UnknownEffort);
            }

            alias = Alias.ForEffort(name, parsedEffort, addedAt);
        }
        else
        {
            if (agent is null || model is null)
            {
                return AliasResult.Failed(AliasFailure.Incomplete);
            }

            if (!Agents.TryParse(agent, out var parsedAgent))
            {
                return AliasResult.Failed(AliasFailure.UnknownAgent);
            }

            if (!AliasShape.IsValidModel(model))
            {
                return AliasResult.Failed(AliasFailure.InvalidModel);
            }

            alias = Alias.ForModel(name, parsedAgent, model, addedAt);
        }

        var (stored, added) = await configuration.AddAliasAsync(alias, cancellationToken);
        if (added)
        {
            return AliasResult.Done(stored, changed: true);
        }

        return stored.Defines(alias) ? AliasResult.Done(stored, changed: false) : AliasResult.Failed(AliasFailure.DefinedDifferently);
    }

    public async Task<AliasResult> RemoveAsync(string name, CancellationToken cancellationToken)
    {
        if (!AliasShape.IsValidName(name))
        {
            return AliasResult.Failed(AliasFailure.NotDefined);
        }

        var removed = await configuration.RemoveAliasAsync(name, cancellationToken);
        return removed is null ? AliasResult.Failed(AliasFailure.NotDefined) : AliasResult.Done(removed, changed: true);
    }
}

/// <summary>An Alias as stored, and whether this write created it (false: an Alias of that name was already stored, and is returned unchanged).</summary>
public readonly record struct AliasWrite(Alias Alias, bool Added);
