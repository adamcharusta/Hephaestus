using System.Globalization;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Settings;
using Hephaestus.Core.Trust;

namespace Hephaestus.Host.Api;

/// <summary>Core's types as the documents the API answers with, the same shape on the API and in <c>--json</c> output.</summary>
internal static class Documents
{
    /// <summary>UTC ISO 8601, to the second.</summary>
    public static string Time(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static TrustedUserDocument TrustedUser(TrustedUser user) => new(user.UserId, user.Login, Time(user.AddedAt));

    public static AliasDocument Alias(Alias alias) => new(alias.Name, alias.Agent?.Tag(), alias.Model, alias.Effort?.Tag(), Time(alias.AddedAt));

    public static LimitsDocument Limits(InstallationLimits limits) => new(
        limits.TaskDuration is { } duration ? LimitFormat.FormatDuration(duration) : null,
        limits.CpuMillis is { } cpus ? LimitFormat.FormatCpus(cpus) : null,
        limits.MemoryBytes is { } memory ? LimitFormat.FormatMemory(memory) : null);

    public static SettingDocument Setting(SettingValue setting) => new(setting.Name, setting.Value, setting.Default, setting.IsSet);

    /// <param name="change">What a registration did; null when the document is not the answer to one.</param>
    /// <param name="baseBranchProtected">What GitHub reports of the Base Branch; known only when the Project was registered just now.</param>
    public static ProjectDocument Project(Project project, ProjectChange? change = null, bool? baseBranchProtected = null) => new(
        project.RepositoryId,
        project.FullName,
        project.BaseBranch,
        Time(project.RegisteredAt),
        project.RemovedAt is { } removed ? Time(removed) : null,
        change switch { ProjectChange.Registered => "registered", ProjectChange.Updated => "updated", ProjectChange.Unchanged => "unchanged", _ => null },
        baseBranchProtected);
}
