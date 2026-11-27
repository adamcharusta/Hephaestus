using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Methodologies;

/// <summary>
/// A Methodology as the image ships it (AD-14): its name and its files, relative to its directory with forward slashes. The files are what the runtime places
/// read-only at <see cref="StepContainerLayout.Methodology"/>; the instruction templates are among them.
/// </summary>
public sealed record Methodology(string Name, IReadOnlyList<WorkspaceFile> Files);

/// <summary>
/// Which instruction template a step applied (AD-14): the file in the Methodology and the SHA-256 of its bytes in lower-case hexadecimal, so that an edit of a
/// template is a different identity.
/// </summary>
public sealed record TemplateIdentity(string Path, string Hash)
{
    public override string ToString() => $"{Path}@sha256:{Hash}";
}

/// <summary>The instruction template Core selected for a step kind, with the bytes that become <c>instruction.md</c> unchanged.</summary>
public sealed record MethodologyTemplate(TemplateIdentity Identity, byte[] Content);
