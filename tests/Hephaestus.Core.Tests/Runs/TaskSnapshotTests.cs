using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Runs;

/// <summary>Story 3.14, AD-16, FR-2: what a Task started with is stored as one document and read back whole, and a document that is not one of ours is no snapshot.</summary>
public class TaskSnapshotTests
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset Noon = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    private static TaskSnapshot Snapshot(ProjectLimits? limits = null, Language? language = null) => new(
        TaskId,
        new string('a', 40),
        new ProjectConfiguration("opus", null, "deep", "default", ["dotnet build"], 2, "ubuntu:24.04", limits ?? ProjectLimits.None, ["nuget.org"], language),
        new ResolvedImage("ubuntu:24.04", "sha256:" + new string('1', 64), AgentArchitecture.Arm64, CLibrary.Musl),
        Noon);

    [Fact]
    public void A_snapshot_is_read_back_as_it_was_written()
    {
        var written = Snapshot(new ProjectLimits(TimeSpan.FromMinutes(90), 2500, 4L * 1024 * 1024 * 1024), Language.Polish);

        Assert.True(TaskSnapshot.TryParse(TaskId, Noon, written.ToJson(), out var read));

        Assert.Equal((written.CommitId, written.Image, written.TaskId, written.TakenAt), (read.CommitId, read.Image, read.TaskId, read.TakenAt));
        Assert.Equal((written.Configuration.PrimaryAlias, written.Configuration.FallbackAlias, written.Configuration.Effort, written.Configuration.Methodology, written.Configuration.CorrectionRounds, written.Configuration.Image, written.Configuration.Limits, written.Configuration.Language),
            (read.Configuration.PrimaryAlias, read.Configuration.FallbackAlias, read.Configuration.Effort, read.Configuration.Methodology, read.Configuration.CorrectionRounds, read.Configuration.Image, read.Configuration.Limits, read.Configuration.Language));
        Assert.Equal(["dotnet build"], read.Configuration.Validation);
        Assert.Equal(["nuget.org"], read.Configuration.AllowedEgressHosts);
    }

    [Fact]
    public void A_setting_the_file_did_not_name_is_absent_in_the_document_and_stays_absent()
    {
        Assert.True(TaskSnapshot.TryParse(TaskId, Noon, Snapshot().ToJson(), out var read));

        Assert.Equal((null, null, null, ProjectLimits.None), (read.Configuration.FallbackAlias, read.Configuration.Language, read.Configuration.Limits.TaskDuration, read.Configuration.Limits));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"commitId":"a","unknown":1}""")]
    public void A_document_that_is_not_a_snapshot_is_refused(string? json)
    {
        Assert.False(TaskSnapshot.TryParse(TaskId, Noon, json, out _));
    }

    [Theory]
    [InlineData("Arm64", "Plan9", null)]
    [InlineData("Sparc", "Musl", null)]
    [InlineData("Arm64", "Musl", "klingon")]
    public void A_document_with_a_value_this_version_does_not_know_is_refused(string architecture, string libc, string? language)
    {
        var json = Snapshot().ToJson().Replace("\"Arm64\"", $"\"{architecture}\"", StringComparison.Ordinal).Replace("\"Musl\"", $"\"{libc}\"", StringComparison.Ordinal);
        if (language is not null)
        {
            json = json.Replace("\"language\":null", $"\"language\":\"{language}\"", StringComparison.Ordinal);
        }

        Assert.False(TaskSnapshot.TryParse(TaskId, Noon, json, out _));
    }
}
