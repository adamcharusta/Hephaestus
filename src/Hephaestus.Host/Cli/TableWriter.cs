namespace Hephaestus.Host.Cli;

/// <summary>Writes a table the way every list of the command line is written: a header row, columns aligned with two spaces (UX "Output").</summary>
internal static class TableWriter
{
    /// <summary>The first row is the header. A cell that is absent is written as <c>none</c> by the caller, never as an empty cell.</summary>
    public static void Write(TextWriter output, IReadOnlyList<string[]> rows)
    {
        var columns = rows[0].Length;
        var widths = Enumerable.Range(0, columns).Select(column => rows.Max(row => row[column].Length)).ToArray();
        foreach (var row in rows)
        {
            output.WriteLine(string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd());
        }
    }
}
