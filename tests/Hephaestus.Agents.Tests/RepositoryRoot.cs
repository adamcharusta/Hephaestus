namespace Hephaestus.Agents.Tests;

/// <summary>Finds the repository root from the test's output directory.</summary>
internal static class RepositoryRoot
{
    public const string SolutionFile = "Hephaestus.slnx";

    public static string Path { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"{SolutionFile} was not found above {AppContext.BaseDirectory}.");
    }
}
