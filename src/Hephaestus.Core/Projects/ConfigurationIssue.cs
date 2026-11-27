using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Projects;

/// <summary>
/// One thing wrong with a configuration file: the setting by its key (<c>limits.memory</c>, <c>validation[1]</c>, or the file's name when the
/// whole file is meant), the line it is on when there is one, and the reason as a message id with parameters (AD-21). The setting is a
/// key and is never translated.
/// </summary>
public sealed record ConfigurationIssue(string Setting, int? Line, Message Reason);
