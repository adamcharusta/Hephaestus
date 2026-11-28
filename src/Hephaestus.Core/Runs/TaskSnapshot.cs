using System.Text.Json;
using System.Text.Json.Serialization;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Runs;

/// <summary>
/// What a Task started with (AD-16, FR-2): the Project Configuration as it was on the Base Branch, the commit it was read at, and the image it resolved to, with its digest. Every later step of the Task
/// uses this and never the file again, so a change to the file on the Base Branch cannot change the rules of a run that began before it. The commit is also the Task's start commit.
/// </summary>
/// <param name="CommitId">The Base Branch's commit the file was read at; the tip the Task's first checkout is made from.</param>
public sealed record TaskSnapshot(Guid TaskId, string CommitId, ProjectConfiguration Configuration, ResolvedImage Image, DateTimeOffset TakenAt)
{
    /// <summary>The document the snapshot is stored as.</summary>
    public string ToJson() => JsonSerializer.Serialize(SnapshotDocument.From(this), SnapshotJson.Default.SnapshotDocument);

    /// <summary>Reads the stored document back; false when it is not one this version wrote.</summary>
    public static bool TryParse(Guid taskId, DateTimeOffset takenAt, string? json, out TaskSnapshot snapshot)
    {
        snapshot = null!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            if (JsonSerializer.Deserialize(json, SnapshotJson.Default.SnapshotDocument) is { } document && document.TryRead(taskId, takenAt, out snapshot!))
            {
                return true;
            }
        }
        catch (JsonException)
        {
            // A document that is not one of ours is not a snapshot.
        }

        return false;
    }
}

internal sealed record ImageDocument(string Reference, string Digest, string Architecture, string Libc);

internal sealed record SnapshotDocument(
    string CommitId,
    ImageDocument Image,
    string? PrimaryAlias,
    string? FallbackAlias,
    string? Effort,
    string Methodology,
    IReadOnlyList<string> Validation,
    int CorrectionRounds,
    string ConfiguredImage,
    long? TaskDurationSeconds,
    int? CpuMillis,
    long? MemoryBytes,
    IReadOnlyList<string> AllowedEgressHosts,
    string? Language)
{
    public static SnapshotDocument From(TaskSnapshot snapshot)
    {
        var configuration = snapshot.Configuration;
        return new SnapshotDocument(
            snapshot.CommitId,
            new ImageDocument(snapshot.Image.Reference, snapshot.Image.Digest, snapshot.Image.Architecture.ToString(), snapshot.Image.Libc.ToString()),
            configuration.PrimaryAlias,
            configuration.FallbackAlias,
            configuration.Effort,
            configuration.Methodology,
            configuration.Validation,
            configuration.CorrectionRounds,
            configuration.Image,
            configuration.Limits.TaskDuration is { } duration ? (long)duration.TotalSeconds : null,
            configuration.Limits.CpuMillis,
            configuration.Limits.MemoryBytes,
            configuration.AllowedEgressHosts,
            configuration.Language?.Tag());
    }

    public bool TryRead(Guid taskId, DateTimeOffset takenAt, out TaskSnapshot snapshot)
    {
        snapshot = null!;
        if (CommitId is null || Image is null || Methodology is null || Validation is null || ConfiguredImage is null || AllowedEgressHosts is null
            || !Enum.TryParse<AgentArchitecture>(Image.Architecture, out var architecture)
            || !Enum.TryParse<CLibrary>(Image.Libc, out var libc))
        {
            return false;
        }

        Language? language = null;
        if (Language is not null)
        {
            if (!Languages.TryParse(Language, out var parsed))
            {
                return false;
            }

            language = parsed;
        }

        var limits = new ProjectLimits(TaskDurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null, CpuMillis, MemoryBytes);
        var configuration = new ProjectConfiguration(PrimaryAlias, FallbackAlias, Effort, Methodology, Validation, CorrectionRounds, ConfiguredImage, limits, AllowedEgressHosts, language);
        snapshot = new TaskSnapshot(taskId, CommitId, configuration, new ResolvedImage(Image.Reference, Image.Digest, architecture, libc), takenAt);
        return true;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SnapshotDocument))]
internal sealed partial class SnapshotJson : JsonSerializerContext;
