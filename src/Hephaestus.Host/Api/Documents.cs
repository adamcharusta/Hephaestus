using System.Globalization;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Readiness;
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

    public static AgentDocument Agent(AgentStatus status) => new(
        status.Agent.Tag(),
        status.Mode?.Tag(),
        status.ChosenAt is { } chosen ? Time(chosen) : null,
        status.Secret,
        status.CredentialStored,
        status.Capacity?.Readiness.Tag(),
        status.Capacity is { } capacity ? Time(capacity.ProbedAt) : null,
        status.Capacity is { ReasonId: { } reason } found ? new AgentReasonDocument(reason, found.ReasonArguments) : null);

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

    public static ConfigurationCheckDocument ConfigurationCheck(Project project, ConfigurationStatus status, string commitId, IReadOnlyList<ConfigurationIssue> issues, ProjectConfiguration? configuration) => new(
        project.RepositoryId,
        project.FullName,
        project.BaseBranch,
        status switch { ConfigurationStatus.Valid => "valid", ConfigurationStatus.Invalid => "invalid", _ => "missing" },
        commitId,
        [.. issues.Select(issue => new ConfigurationIssueDocument(issue.Setting, issue.Line, issue.Reason.Id, [.. issue.Reason.Arguments.Select(argument => Convert.ToString(argument, CultureInfo.InvariantCulture) ?? string.Empty)]))],
        configuration is null ? null : Settings(configuration));

    private static ProjectConfigurationDocument Settings(ProjectConfiguration configuration) => new(
        configuration.PrimaryAlias,
        configuration.FallbackAlias,
        configuration.Effort,
        configuration.Methodology,
        configuration.Validation,
        configuration.CorrectionRounds,
        configuration.Image,
        new ProjectLimitsDocument(
            configuration.Limits.TaskDuration is { } duration ? LimitFormat.FormatDuration(duration) : null,
            configuration.Limits.CpuMillis is { } cpus ? LimitFormat.FormatCpus(cpus) : null,
            configuration.Limits.MemoryBytes is { } memory ? LimitFormat.FormatMemory(memory) : null),
        configuration.AllowedEgressHosts,
        configuration.Language?.Tag());
}
