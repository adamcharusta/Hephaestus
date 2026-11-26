using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Settings;

/// <summary>Why a setting was not changed.</summary>
public enum SettingFailure
{
    /// <summary>It was.</summary>
    None,

    /// <summary>No such setting.</summary>
    UnknownSetting,

    /// <summary>The value is not one the setting accepts.</summary>
    InvalidValue,
}

/// <summary>A setting as the Owner sees it: its value now (the default until one is stored), its default, and whether the Owner has set it.</summary>
public sealed record SettingValue(string Name, string Value, string Default, bool IsSet);

/// <summary>The outcome of changing a setting; <see cref="Setting"/> is the setting as stored now.</summary>
public sealed record SettingResult(SettingFailure Failure, SettingValue? Setting)
{
    public static SettingResult Failed(SettingFailure failure) => new(failure, null);

    public static SettingResult Done(SettingValue setting) => new(SettingFailure.None, setting);
}

/// <summary>The rules for the installation settings: a name must be a known setting and a value one it accepts.</summary>
public sealed class SettingService(IReadStore reads, IInstallationConfiguration configuration)
{
    public async Task<IReadOnlyList<SettingValue>> ListAsync(CancellationToken cancellationToken)
    {
        var stored = await reads.ListSettingsAsync(cancellationToken);
        return [.. InstallationSettings.All.Select(setting => Value(setting, stored))];
    }

    public async Task<SettingResult> SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        if (InstallationSettings.Find(name) is not { } setting)
        {
            return SettingResult.Failed(SettingFailure.UnknownSetting);
        }

        if (setting.Accept(value) is not { } accepted)
        {
            return SettingResult.Failed(SettingFailure.InvalidValue);
        }

        await configuration.SetSettingAsync(setting.Name, accepted, cancellationToken);
        return SettingResult.Done(new SettingValue(setting.Name, accepted, setting.Default, IsSet: true));
    }

    private static SettingValue Value(SettingDefinition setting, IReadOnlyDictionary<string, string> stored) =>
        stored.TryGetValue(setting.Name, out var value) && setting.Accept(value) is { } accepted
            ? new SettingValue(setting.Name, accepted, setting.Default, IsSet: true)
            : new SettingValue(setting.Name, setting.Default, setting.Default, IsSet: false);
}
