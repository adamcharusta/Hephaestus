namespace Hephaestus.Host.Tests.Architecture;

/// <summary>
/// What the architecture rules need to know about one project. <paramref name="Packages"/> is the full transitive
/// closure of packages the project resolves; <paramref name="DirectPackages"/> are the ones its project file names.
/// <paramref name="FrameworkReferences"/> are the shared frameworks the project, or a package it resolves, requires.
/// </summary>
internal sealed record ProjectNode(
    string Name,
    IReadOnlySet<string> ProjectReferences,
    IReadOnlySet<string> Packages,
    IReadOnlySet<string> DirectPackages,
    IReadOnlySet<string> FrameworkReferences)
{
    /// <summary>A node for a modelled graph; a package not listed as direct is transitive.</summary>
    public static ProjectNode Create(
        string name,
        IEnumerable<string>? projectReferences = null,
        IEnumerable<string>? packages = null,
        IEnumerable<string>? frameworkReferences = null,
        IEnumerable<string>? directPackages = null)
    {
        var all = (packages ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var direct = (directPackages ?? all).ToHashSet(StringComparer.OrdinalIgnoreCase);
        all.UnionWith(direct);
        return new ProjectNode(
            name,
            (projectReferences ?? []).ToHashSet(StringComparer.Ordinal),
            all,
            direct,
            (frameworkReferences ?? []).ToHashSet(StringComparer.Ordinal));
    }
}
