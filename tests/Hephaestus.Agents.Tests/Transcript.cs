namespace Hephaestus.Agents.Tests;

/// <summary>The recorded transcripts under <c>Transcripts</c>: the stream a CLI printed, in a file named for the CLI, its version, and the run.</summary>
internal static class Transcript
{
    public const string Version = "2.1.332";

    public static string[] Lines(string run) => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Transcripts", $"claude-code-{Version}-{run}.jsonl"));
}
