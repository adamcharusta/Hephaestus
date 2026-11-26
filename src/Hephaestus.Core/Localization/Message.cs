namespace Hephaestus.Core.Localization;

/// <summary>A sentence for a person, not yet written: an id from <see cref="MessageIds"/> and its parameters (AD-21).</summary>
public readonly record struct Message(string Id, params object[] Arguments);
