using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Protection;

/// <summary>Why a change to the protected paths did not happen.</summary>
public enum ProtectedPathFailure
{
    /// <summary>It happened.</summary>
    None,

    /// <summary>The glob is not shaped like a protected path (<see cref="ProtectedPath.IsValid"/>).</summary>
    InvalidGlob,

    /// <summary>The glob is not on the list.</summary>
    NotProtected,
}

/// <summary>The outcome of adding or removing a protected path.</summary>
/// <param name="Changed">False when <c>add</c> found the glob already listed.</param>
public sealed record ProtectedPathResult(ProtectedPathFailure Failure, string? Glob, bool Changed)
{
    public static ProtectedPathResult Failed(ProtectedPathFailure failure) => new(failure, null, Changed: false);

    public static ProtectedPathResult Done(string glob, bool changed) => new(ProtectedPathFailure.None, glob, changed);
}

/// <summary>The rules for the protected paths: an entry is a glob of a safe shape, and the Owner may change the list freely.</summary>
public sealed class ProtectedPathService(IInstallationConfiguration configuration)
{
    public async Task<ProtectedPathResult> AddAsync(string glob, CancellationToken cancellationToken)
    {
        if (!ProtectedPath.IsValid(glob))
        {
            return ProtectedPathResult.Failed(ProtectedPathFailure.InvalidGlob);
        }

        return ProtectedPathResult.Done(glob, await configuration.AddProtectedPathAsync(glob, cancellationToken));
    }

    public async Task<ProtectedPathResult> RemoveAsync(string glob, CancellationToken cancellationToken)
    {
        if (!ProtectedPath.IsValid(glob))
        {
            return ProtectedPathResult.Failed(ProtectedPathFailure.NotProtected);
        }

        return await configuration.RemoveProtectedPathAsync(glob, cancellationToken)
            ? ProtectedPathResult.Done(glob, changed: true)
            : ProtectedPathResult.Failed(ProtectedPathFailure.NotProtected);
    }
}
