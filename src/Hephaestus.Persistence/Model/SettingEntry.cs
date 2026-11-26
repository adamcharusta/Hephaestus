namespace Hephaestus.Persistence.Model;

/// <summary>An installation setting the Owner has set, by name. A setting that was never set has no row and has its default.</summary>
internal sealed class SettingEntry
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
