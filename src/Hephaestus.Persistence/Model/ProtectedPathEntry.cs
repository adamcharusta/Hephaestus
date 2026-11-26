namespace Hephaestus.Persistence.Model;

/// <summary>A protected path (AD-19): a glob, which is also its key.</summary>
internal sealed class ProtectedPathEntry
{
    public string Pattern { get; set; } = string.Empty;
}
