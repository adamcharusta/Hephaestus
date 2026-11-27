namespace Hephaestus.Core.Workspaces;

/// <summary>
/// One file the runtime places read-only in a step container, at <see cref="StepContainerLayout.Context"/> or
/// <see cref="StepContainerLayout.Methodology"/>. The path is relative to that directory, uses forward slashes, and cannot leave it.
/// </summary>
public sealed record WorkspaceFile
{
    public WorkspaceFile(string path, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!IsContained(path))
        {
            throw new ArgumentException($"'{path}' is not a relative path inside the directory it is placed in.", nameof(path));
        }

        Path = path;
        Content = content;
    }

    public string Path { get; }

    public byte[] Content { get; }

    /// <summary>A path of one or more names separated by <c>/</c>, none of them empty, <c>.</c>, or <c>..</c>, and none holding a backslash or a control character.</summary>
    public static bool IsContained(string? path) =>
        !string.IsNullOrEmpty(path)
        && path.Split('/').All(name => name.Length > 0 && name is not ("." or "..") && !name.Any(character => character == '\\' || char.IsControl(character)));
}
