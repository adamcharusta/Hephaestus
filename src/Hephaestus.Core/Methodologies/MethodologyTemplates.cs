using System.Security.Cryptography;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Methodologies;

/// <summary>
/// The selection of an instruction template (AD-14, FR-62): by step kind alone. Nothing here reads the Methodology's name or an Agent, so adding a Methodology
/// changes no code and moving a Task between Agents changes no instruction.
/// </summary>
public static class MethodologyTemplates
{
    /// <summary>The file of a step kind's template in a Methodology's directory: the kind's tag and <c>.md</c>.</summary>
    public static string FileName(StepKind kind) => kind.Tag() + ".md";

    /// <summary>The Methodology's template for the kind, or null when it has none: a Methodology supports the kinds it has a template for.</summary>
    public static MethodologyTemplate? Select(Methodology methodology, StepKind kind)
    {
        ArgumentNullException.ThrowIfNull(methodology);
        var path = FileName(kind);
        var file = methodology.Files.FirstOrDefault(candidate => candidate.Path == path);
        return file is null ? null : new MethodologyTemplate(new TemplateIdentity(path, Convert.ToHexStringLower(SHA256.HashData(file.Content))), file.Content);
    }
}
