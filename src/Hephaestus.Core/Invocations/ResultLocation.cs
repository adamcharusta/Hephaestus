namespace Hephaestus.Core.Invocations;

/// <summary>Where an invocation leaves its result.</summary>
public enum ResultSource
{
    /// <summary>In the stream the CLI prints, which the runtime stores and the adapter parses.</summary>
    OutputStream,

    /// <summary>In a file of the step container.</summary>
    File,
}

/// <summary>Where the result of an invocation is found (AD-10, AD-13).</summary>
public sealed record ResultLocation
{
    private ResultLocation(ResultSource source, string? path)
    {
        Source = source;
        Path = path;
    }

    public ResultSource Source { get; }

    /// <summary>The file's absolute path in the step container; null for the output stream.</summary>
    public string? Path { get; }

    public static ResultLocation InOutputStream { get; } = new(ResultSource.OutputStream, null);

    public static ResultLocation InFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return path.StartsWith('/') ? new ResultLocation(ResultSource.File, path) : throw new ArgumentException("A result file is named by an absolute path of the step container.", nameof(path));
    }
}
