namespace Hephaestus.Host.Tests.Architecture;

/// <summary>
/// The dependency direction of AD-3: adapters reference only Core, only Host references adapters, and Core references no
/// adapter and no Octokit, Docker, EF Core, or ASP.NET Core package. Each vendor library also stays in the one adapter
/// that owns it, because only that adapter talks to the vendor.
/// </summary>
internal static class BoundaryRules
{
    public const string Core = "Hephaestus.Core";
    public const string Host = "Hephaestus.Host";

    public static readonly IReadOnlyList<string> Adapters =
    [
        "Hephaestus.Agents",
        "Hephaestus.GitHub",
        "Hephaestus.Workspaces",
        "Hephaestus.Persistence",
    ];

    /// <summary>
    /// A vendor library, how its package names are recognised, and the one project allowed to name it. Host resolves every
    /// adapter's libraries transitively, so for Host only a package it names itself counts.
    /// </summary>
    private static readonly (string Contains, bool AtStart, string Owner)[] VendorPackages =
    [
        ("Octokit", true, "Hephaestus.GitHub"),
        ("Docker.", true, "Hephaestus.Workspaces"),
        ("EntityFrameworkCore", false, "Hephaestus.Persistence"),
        ("Microsoft.Data.Sqlite", true, "Hephaestus.Persistence"),
        ("AspNetCore", false, Host),
    ];

    public static IReadOnlyList<string> Check(IReadOnlyCollection<ProjectNode> projects)
    {
        var violations = new List<string>();
        var known = projects.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var project in projects)
        {
            foreach (var reference in project.ProjectReferences.Order(StringComparer.Ordinal))
            {
                if (!IsAllowedProjectReference(project.Name, reference, known))
                {
                    violations.Add($"{project.Name} references project {reference}, which AD-3 does not allow.");
                }
            }

            foreach (var (text, atStart, owner) in VendorPackages)
            {
                if (project.Name == owner)
                {
                    continue;
                }

                var candidates = project.Name == Host ? project.DirectPackages : project.Packages;
                var comparison = StringComparison.OrdinalIgnoreCase;
                foreach (var package in candidates.Where(p => atStart ? p.StartsWith(text, comparison) : p.Contains(text, comparison)).Order(StringComparer.Ordinal))
                {
                    var rule = project.Name == Core ? "Core references no such package" : $"only {owner} may name it";
                    violations.Add($"{project.Name} resolves package {package}; {rule}.");
                }
            }

            if (project.Name != Host)
            {
                foreach (var framework in project.FrameworkReferences.Where(f => f.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
                {
                    violations.Add($"{project.Name} uses framework {framework}; only {Host} may.");
                }
            }
        }

        return violations;
    }

    private static bool IsAllowedProjectReference(string project, string reference, HashSet<string> known)
    {
        if (!known.Contains(reference))
        {
            return false;
        }

        return project switch
        {
            Core => false,
            Host => true,
            _ when Adapters.Contains(project) => reference == Core,
            _ => false,
        };
    }
}
