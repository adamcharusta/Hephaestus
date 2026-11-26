using System.Globalization;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
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
}
