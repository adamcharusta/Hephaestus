using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Deployment;

/// <summary>
/// Story 1.11, AD-14: the Methodologies the image ships reach it through three lines that no run of the tests exercises, because the tests do not build the image.
/// If one were dropped, the container would offer no Methodology and every configuration file would be told so.
/// </summary>
public class ImageBuildContextTests
{
    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([RepositoryRoot.Path, .. path]));

    [Fact]
    public void The_build_context_lets_the_methodologies_directory_in()
    {
        var lines = Read(".dockerignore").Split('\n', StringSplitOptions.TrimEntries);

        Assert.Contains("!methodologies/**", lines);
    }

    [Fact]
    public void The_Dockerfile_copies_the_methodologies_directory_into_the_build_before_it_publishes()
    {
        var lines = Read("deploy", "Dockerfile").Split('\n', StringSplitOptions.TrimEntries);

        var copy = Array.IndexOf(lines, "COPY methodologies/ methodologies/");
        var publish = Array.FindIndex(lines, line => line.StartsWith("RUN dotnet publish", StringComparison.Ordinal));
        Assert.True(copy >= 0, "deploy/Dockerfile does not copy methodologies/.");
        Assert.True(copy < publish, "methodologies/ must be copied before dotnet publish, which places it beside the executable.");
    }

    [Fact]
    public void The_Host_project_places_the_methodologies_beside_the_executable()
    {
        var project = Read("src", "Hephaestus.Host", "Hephaestus.Host.csproj");

        Assert.Contains("""Link="methodologies\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" """.Trim(), project, StringComparison.Ordinal);
    }
}
