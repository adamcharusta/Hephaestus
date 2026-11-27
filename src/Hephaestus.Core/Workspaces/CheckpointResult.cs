namespace Hephaestus.Core.Workspaces;

/// <summary>
/// What a checkpoint came to (AD-9): a bundle of one commit, or nothing because the tree was the last pushed tip's. The bundle is a file of the data volume that the runtime wrote and the loop deletes once the push
/// result is journaled; its reference carries the checkpoint commit as <see cref="BundleReference.Tip"/> and the last pushed tip as <see cref="BundleReference.Prerequisite"/>.
/// </summary>
public sealed record CheckpointResult
{
    private CheckpointResult(BundleReference? bundle) => Bundle = bundle;

    /// <summary>The tree was unchanged: no commit and no bundle were made.</summary>
    public static CheckpointResult Unchanged { get; } = new((BundleReference?)null);

    public static CheckpointResult Made(BundleReference bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return bundle.Prerequisite is null
            ? throw new ArgumentException("A checkpoint's bundle has the last pushed tip as its prerequisite.", nameof(bundle))
            : new CheckpointResult(bundle);
    }

    /// <summary>The bundle, or null when the tree was unchanged.</summary>
    public BundleReference? Bundle { get; }

    public bool IsUnchanged => Bundle is null;
}
