using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Localization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Hephaestus.Core.Projects;

/// <summary>What a configuration file is checked against: the installation's own Aliases and limits, and the Methodologies it ships.</summary>
public sealed record ConfigurationContext(IReadOnlyList<Alias> Aliases, InstallationLimits Limits, IReadOnlyList<string> Methodologies);

/// <summary>The settings of a valid file, or every problem found; exactly one of the two.</summary>
public sealed record ConfigurationParse(ProjectConfiguration? Configuration, IReadOnlyList<ConfigurationIssue> Issues)
{
    public bool IsValid => Configuration is not null;
}

/// <summary>
/// Parses and validates the bytes of <c>hephaestus.yml</c> (FR-2, AD-16). It is a pure function of the bytes and the
/// <see cref="ConfigurationContext"/>: it reads no repository, runs nothing the file names, and reports every problem it finds, not the first.
/// A value from the file is shortened and stripped of control characters before a message carries it (<see cref="ConfigurationText"/>), so that a file cannot write into a terminal.
/// </summary>
public static partial class ProjectConfigurationParser
{
    private const int MaximumNameLength = 255;
    private const int MaximumHostLength = 253;

    public static ConfigurationParse Parse(byte[] content, ConfigurationContext context)
    {
        var issues = new List<ConfigurationIssue>();
        var file = ProjectConfigurationFile.Path;

        if (content.Length > ProjectConfigurationFile.MaximumLength)
        {
            return Failed(file, null, MessageIds.ConfigIssueTooLarge, ProjectConfigurationFile.MaximumLength.ToString(CultureInfo.InvariantCulture));
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return Failed(file, null, MessageIds.ConfigIssueNotUtf8);
        }

        text = text.TrimStart('\uFEFF');
        if (CheckStructure(text, file) is { } structural)
        {
            return new ConfigurationParse(null, [structural]);
        }

        YamlNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode;
        }
        catch (YamlException exception)
        {
            // The text is known to be well formed (CheckStructure), so this is not expected; it is still a fault of the file, not of the host.
            return Failed(file, LineOf(exception.Start), MessageIds.ConfigIssueSyntax, Text(exception.Start.Line), Text(exception.Start.Column));
        }

        // A file with nothing in it, or only a document marker, names no setting; what it lacks is reported below like any other.
        if (root is null || IsNull(root))
        {
            root = new YamlMappingNode();
        }

        if (root is not YamlMappingNode mapping)
        {
            return Failed(file, Line(root), MessageIds.ConfigIssueNotMapping);
        }

        var reader = new Reader(context, issues);
        return reader.Read(mapping);
    }

    /// <summary>
    /// The shape of the whole stream before any node is built, from its events alone: it is YAML, it is one document, it is not nested deeper than
    /// any setting is, it uses no alias (an alias can make a key that contains itself, which the node model cannot hash), and no key is written twice in a mapping.
    /// </summary>
    private static ConfigurationIssue? CheckStructure(string text, string file)
    {
        try
        {
            var parser = new Parser(new StringReader(text));
            int documents = 0;

            // One frame per open collection: the keys of a mapping so far, and whether the next node of it is a key.
            var frames = new Stack<(HashSet<string>? Keys, bool[] ExpectKey)>();
            while (parser.MoveNext())
            {
                var current = parser.Current!;
                if (current is AnchorAlias)
                {
                    return new ConfigurationIssue(file, LineOf(current.Start), new Message(MessageIds.ConfigIssueAliasNotAllowed));
                }

                if (current is DocumentStart && ++documents > 1)
                {
                    return new ConfigurationIssue(file, LineOf(current.Start), new Message(MessageIds.ConfigIssueSeveralDocuments));
                }

                if (current is SequenceEnd or MappingEnd)
                {
                    frames.Pop();
                    continue;
                }

                if (current is not (Scalar or SequenceStart or MappingStart))
                {
                    continue;
                }

                if (frames.Count > 0 && frames.Peek() is { Keys: { } keys, ExpectKey: var expectKey })
                {
                    if (expectKey[0] && current is Scalar { Value: var key } && !keys.Add(key))
                    {
                        return new ConfigurationIssue(file, LineOf(current.Start), new Message(MessageIds.ConfigIssueDuplicateKey));
                    }

                    expectKey[0] = !expectKey[0];
                }

                if (current is SequenceStart or MappingStart)
                {
                    if (frames.Count >= ProjectConfigurationFile.MaximumNesting)
                    {
                        return new ConfigurationIssue(file, LineOf(current.Start), new Message(MessageIds.ConfigIssueTooDeep, ProjectConfigurationFile.MaximumNesting.ToString(CultureInfo.InvariantCulture)));
                    }

                    frames.Push((current is MappingStart ? new HashSet<string>(StringComparer.Ordinal) : null, [true]));
                }
            }

            return null;
        }
        catch (YamlException exception)
        {
            return new ConfigurationIssue(file, LineOf(exception.Start), new Message(MessageIds.ConfigIssueSyntax, Text(exception.Start.Line), Text(exception.Start.Column)));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or IndexOutOfRangeException)
        {
            // The scanner of the library can fail on a malformed document with an exception of its own instead of a YamlException, and where it was cannot be said.
            return new ConfigurationIssue(file, null, new Message(MessageIds.ConfigIssueNotYaml));
        }
    }

    private static ConfigurationParse Failed(string setting, int? line, string id, params string[] arguments) =>
        new(null, [new ConfigurationIssue(setting, line, new Message(id, [.. arguments]))]);

    private static int? Line(YamlNode node) => LineOf(node.Start);

    /// <summary>A setting written with no value: nothing after the colon, <c>~</c>, or <c>null</c>.</summary>
    private static bool IsNull(YamlNode node) => node is YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" or "~" or "null" or "Null" or "NULL" };

    private static int? LineOf(Mark mark) => mark.Line is > 0 and <= int.MaxValue ? (int)mark.Line : null;

    private static string Text(long number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="host"/> is a host name: dot-separated labels of letters, digits, and inner hyphens, the last with a letter in it, so that an address is not one.</summary>
    internal static bool IsHostName(string host)
    {
        try
        {
            return host.Length is > 0 and <= MaximumHostLength && HostShape().IsMatch(host) && HasLetterInLastLabel(host);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool HasLetterInLastLabel(string host) => host[(host.LastIndexOf('.') + 1)..].Any(char.IsAsciiLetter);

    /// <summary>An image reference: an optional registry (a host with an optional port), lower-case path components, an optional tag, and an optional <c>sha256</c> digest.</summary>
    internal static bool IsImageReference(string image)
    {
        try
        {
            return image.Length is > 0 and <= MaximumNameLength && ImageShape().IsMatch(image);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HostShape();

    [GeneratedRegex(@"^(?:(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*(?::[0-9]{1,5})?/)?[a-z0-9]+(?:(?:\.|_|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:\.|_|__|-+)[a-z0-9]+)*)*(?::[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})?(?:@sha256:[a-f0-9]{64})?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageShape();

    [GeneratedRegex(@"^[0-9]{1,3}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WholeNumber();

    /// <summary>One read of one file: the settings found so far and the problems.</summary>
    private sealed class Reader(ConfigurationContext context, List<ConfigurationIssue> issues)
    {
        private string? _primary, _fallback, _effort, _methodology, _image;
        private List<string>? _validation;
        private int? _rounds;
        private ProjectLimits _limits = ProjectLimits.None;
        private List<string> _hosts = [];
        private Language? _language;
        private readonly Dictionary<string, int?> _lines = [];

        public ConfigurationParse Read(YamlMappingNode mapping)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (keyNode, value) in mapping.Children)
            {
                if (keyNode is not YamlScalarNode { Value: { } key })
                {
                    Add("?", Line(keyNode), MessageIds.ConfigIssueUnknownKey, string.Join(", ", ProjectConfigurationFile.Keys));
                    continue;
                }

                seen.Add(key);
                switch (key)
                {
                    case ProjectConfigurationFile.PrimaryAliasKey:
                        _primary = ReadText(key, value, MaximumNameLength);
                        break;
                    case ProjectConfigurationFile.FallbackAliasKey:
                        _fallback = ReadText(key, value, MaximumNameLength);
                        break;
                    case ProjectConfigurationFile.EffortKey:
                        _effort = ReadText(key, value, MaximumNameLength);
                        break;
                    case ProjectConfigurationFile.MethodologyKey:
                        _methodology = ReadText(key, value, MaximumNameLength);
                        break;
                    case ProjectConfigurationFile.ValidationKey:
                        _validation = ReadCommands(value);
                        break;
                    case ProjectConfigurationFile.CorrectionRoundsKey:
                        _rounds = ReadRounds(value);
                        break;
                    case ProjectConfigurationFile.ImageKey:
                        _image = ReadImage(value);
                        break;
                    case ProjectConfigurationFile.LimitsKey:
                        ReadLimits(value);
                        break;
                    case ProjectConfigurationFile.AllowedEgressHostsKey:
                        ReadHosts(value);
                        break;
                    case ProjectConfigurationFile.LanguageKey:
                        ReadLanguage(value);
                        break;
                    default:
                        Add(ConfigurationText.Safe(key), Line(keyNode), MessageIds.ConfigIssueUnknownKey, string.Join(", ", ProjectConfigurationFile.Keys));
                        break;
                }
            }

            Check(seen);

            // In the order of the file; what has no line (a key that is missing) comes last, and issues of one line keep the order they were found in.
            return issues.Count > 0
                ? new ConfigurationParse(null, [.. issues.OrderBy(issue => issue.Line ?? int.MaxValue)])
                : new ConfigurationParse(
                    new ProjectConfiguration(
                        _primary,
                        _fallback,
                        _effort,
                        _methodology ?? ProjectConfigurationFile.DefaultMethodology,
                        _validation!,
                        _rounds ?? ProjectConfigurationFile.DefaultCorrectionRounds,
                        _image!,
                        _limits,
                        [.. _hosts],
                        _language),
                    []);
        }

        /// <summary>What no single key decides: a key that is required, and the Aliases and the Methodology against the installation's own.</summary>
        private void Check(HashSet<string> seen)
        {
            if (!seen.Contains(ProjectConfigurationFile.ValidationKey))
            {
                Add(ProjectConfigurationFile.ValidationKey, null, MessageIds.ConfigIssueValidationNone);
            }

            if (!seen.Contains(ProjectConfigurationFile.ImageKey))
            {
                Add(ProjectConfigurationFile.ImageKey, null, MessageIds.ConfigIssueRequired);
            }

            var aliases = context.Aliases.ToDictionary(a => a.Name, StringComparer.Ordinal);
            var primary = CheckAgentAlias(ProjectConfigurationFile.PrimaryAliasKey, _primary, aliases);
            var fallback = CheckAgentAlias(ProjectConfigurationFile.FallbackAliasKey, _fallback, aliases);
            if (primary is { } first && fallback is { } second && first == second)
            {
                Add(ProjectConfigurationFile.FallbackAliasKey, LineOf(ProjectConfigurationFile.FallbackAliasKey), MessageIds.ConfigIssueAgentsSame, first.Tag());
            }

            if (_effort is { } effort)
            {
                if (!aliases.TryGetValue(effort, out var alias))
                {
                    Add(ProjectConfigurationFile.EffortKey, LineOf(ProjectConfigurationFile.EffortKey), MessageIds.ConfigIssueAliasUnknown, ConfigurationText.Safe(effort));
                }
                else if (alias.Effort is null)
                {
                    Add(ProjectConfigurationFile.EffortKey, LineOf(ProjectConfigurationFile.EffortKey), MessageIds.ConfigIssueAliasNotEffort, ConfigurationText.Safe(effort));
                }
            }

            // The Methodology that applies, so that one the file does not name is held to the same rule as one it names: an installation that ships none has none to offer.
            var methodology = _methodology ?? ProjectConfigurationFile.DefaultMethodology;
            if (!context.Methodologies.Contains(methodology, StringComparer.Ordinal))
            {
                Add(
                    ProjectConfigurationFile.MethodologyKey,
                    LineOf(ProjectConfigurationFile.MethodologyKey),
                    context.Methodologies.Count == 0 ? MessageIds.ConfigIssueMethodologyNoneShipped : MessageIds.ConfigIssueMethodologyUnavailable,
                    ConfigurationText.Safe(methodology),
                    string.Join(", ", context.Methodologies));
            }
        }

        private int? LineOf(string setting) => _lines.GetValueOrDefault(setting);

        private Agent? CheckAgentAlias(string setting, string? name, Dictionary<string, Alias> aliases)
        {
            if (name is null)
            {
                return null;
            }

            if (!aliases.TryGetValue(name, out var alias))
            {
                Add(setting, LineOf(setting), MessageIds.ConfigIssueAliasUnknown, ConfigurationText.Safe(name));
                return null;
            }

            if (alias.Agent is not { } agent)
            {
                Add(setting, LineOf(setting), MessageIds.ConfigIssueAliasNotAgent, ConfigurationText.Safe(name));
                return null;
            }

            return agent;
        }

        private string? ReadImage(YamlNode node)
        {
            const string setting = ProjectConfigurationFile.ImageKey;
            var image = ReadText(setting, node, MaximumNameLength);
            if (image is not null && !IsImageReference(image))
            {
                Add(setting, Line(node), MessageIds.ConfigIssueImageInvalid, ConfigurationText.Safe(image));
                return null;
            }

            return image;
        }

        private string? ReadText(string setting, YamlNode node, int maximumLength)
        {
            _lines[setting] = Line(node);
            if (node is not YamlScalarNode scalar)
            {
                Add(setting, Line(node), MessageIds.ConfigIssueExpectedText);
                return null;
            }

            var value = scalar.Value?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                Add(setting, Line(node), MessageIds.ConfigIssueEmpty);
                return null;
            }

            if (value.Length > maximumLength)
            {
                Add(setting, Line(node), MessageIds.ConfigIssueTooLong, maximumLength.ToString(CultureInfo.InvariantCulture));
                return null;
            }

            return value;
        }

        private List<string>? ReadCommands(YamlNode node)
        {
            const string setting = ProjectConfigurationFile.ValidationKey;
            if (node is not YamlSequenceNode sequence)
            {
                Add(setting, Line(node), IsNull(node) ? MessageIds.ConfigIssueValidationNone : MessageIds.ConfigIssueExpectedList);
                return null;
            }

            if (sequence.Children.Count == 0)
            {
                Add(setting, Line(node), MessageIds.ConfigIssueValidationNone);
                return null;
            }

            if (sequence.Children.Count > ProjectConfigurationFile.MaximumValidationCommands)
            {
                Add(setting, Line(node), MessageIds.ConfigIssueTooMany, ProjectConfigurationFile.MaximumValidationCommands.ToString(CultureInfo.InvariantCulture));
                return null;
            }

            var commands = new List<string>();
            var faulty = false;
            for (var index = 0; index < sequence.Children.Count; index++)
            {
                var item = $"{setting}[{index.ToString(CultureInfo.InvariantCulture)}]";
                var command = ReadText(item, sequence.Children[index], ProjectConfigurationFile.MaximumCommandLength);
                if (command is null)
                {
                    faulty = true;
                }
                else if (command.Any(c => char.IsControl(c) && c is not ('\n' or '\t' or '\r')))
                {
                    Add(item, Line(sequence.Children[index]), MessageIds.ConfigIssueCommandInvalid);
                    faulty = true;
                }
                else
                {
                    commands.Add(command);
                }
            }

            return faulty ? null : commands;
        }

        private int? ReadRounds(YamlNode node)
        {
            const string setting = ProjectConfigurationFile.CorrectionRoundsKey;
            var text = ReadText(setting, node, MaximumNameLength);
            if (text is null)
            {
                return null;
            }

            if (!WholeNumber().IsMatch(text) || int.Parse(text, CultureInfo.InvariantCulture) > ProjectConfigurationFile.MaximumCorrectionRounds)
            {
                Add(setting, Line(node), MessageIds.ConfigIssueRoundsInvalid, "0", ProjectConfigurationFile.MaximumCorrectionRounds.ToString(CultureInfo.InvariantCulture));
                return null;
            }

            return int.Parse(text, CultureInfo.InvariantCulture);
        }

        private void ReadLimits(YamlNode node)
        {
            const string setting = ProjectConfigurationFile.LimitsKey;
            if (IsNull(node))
            {
                return;
            }

            if (node is not YamlMappingNode limits)
            {
                Add(setting, Line(node), MessageIds.ConfigIssueExpectedMapping);
                return;
            }

            TimeSpan? duration = null;
            int? cpus = null;
            long? memory = null;
            foreach (var (keyNode, value) in limits.Children)
            {
                var key = (keyNode as YamlScalarNode)?.Value;
                var path = $"{setting}.{ConfigurationText.Safe(key ?? "?")}";
                switch (key)
                {
                    case ProjectConfigurationFile.TaskDurationKey:
                        duration = ReadLimit(path, value, LimitFormat.TryParseDuration, MessageIds.ConfigIssueLimitDurationInvalid, LimitFormat.FormatDuration, context.Limits.TaskDuration);
                        break;
                    case ProjectConfigurationFile.CpusKey:
                        cpus = ReadLimit(path, value, LimitFormat.TryParseCpus, MessageIds.ConfigIssueLimitCpusInvalid, LimitFormat.FormatCpus, context.Limits.CpuMillis);
                        break;
                    case ProjectConfigurationFile.MemoryKey:
                        memory = ReadLimit(path, value, LimitFormat.TryParseMemory, MessageIds.ConfigIssueLimitMemoryInvalid, LimitFormat.FormatMemory, context.Limits.MemoryBytes);
                        break;
                    default:
                        Add(path, Line(keyNode), MessageIds.ConfigIssueUnknownKey, string.Join(", ", ProjectConfigurationFile.LimitKeys));
                        break;
                }
            }

            _limits = new ProjectLimits(duration, cpus, memory);
        }

        private delegate bool TryParse<T>(string? text, out T value);

        /// <summary>One limit: well formed, and not above the installation's when it has set one (AD-16).</summary>
        private T? ReadLimit<T>(string setting, YamlNode node, TryParse<T> tryParse, string invalidId, Func<T, string> format, T? ceiling)
            where T : struct, IComparable<T>
        {
            var text = ReadText(setting, node, MaximumNameLength);
            if (text is null)
            {
                return null;
            }

            if (!tryParse(text, out var value))
            {
                Add(setting, Line(node), invalidId, ConfigurationText.Safe(text));
                return null;
            }

            if (ceiling is { } limit && value.CompareTo(limit) > 0)
            {
                Add(setting, Line(node), MessageIds.ConfigIssueLimitAboveInstallation, format(value), format(limit));
                return null;
            }

            return value;
        }

        private void ReadHosts(YamlNode node)
        {
            const string setting = ProjectConfigurationFile.AllowedEgressHostsKey;
            if (IsNull(node))
            {
                return;
            }

            if (node is not YamlSequenceNode sequence)
            {
                Add(setting, Line(node), MessageIds.ConfigIssueExpectedList);
                return;
            }

            if (sequence.Children.Count > ProjectConfigurationFile.MaximumEgressHosts)
            {
                Add(setting, Line(node), MessageIds.ConfigIssueTooMany, ProjectConfigurationFile.MaximumEgressHosts.ToString(CultureInfo.InvariantCulture));
                return;
            }

            var hosts = new List<string>();
            for (var index = 0; index < sequence.Children.Count; index++)
            {
                var item = $"{setting}[{index.ToString(CultureInfo.InvariantCulture)}]";
                if (ReadText(item, sequence.Children[index], MaximumHostLength) is not { } host)
                {
                    continue;
                }

                if (!IsHostName(host))
                {
                    Add(item, Line(sequence.Children[index]), MessageIds.ConfigIssueHostInvalid, ConfigurationText.Safe(host));
                    continue;
                }

                var normalized = host.ToLowerInvariant();
                if (!hosts.Contains(normalized))
                {
                    hosts.Add(normalized);
                }
            }

            _hosts = hosts;
        }

        private void ReadLanguage(YamlNode node)
        {
            const string setting = ProjectConfigurationFile.LanguageKey;
            if (ReadText(setting, node, MaximumNameLength) is not { } text)
            {
                return;
            }

            foreach (var language in Enum.GetValues<Language>())
            {
                if (language.Tag() == text)
                {
                    _language = language;
                    return;
                }
            }

            Add(setting, Line(node), MessageIds.ConfigIssueLanguageInvalid, ConfigurationText.Safe(text));
        }

        private void Add(string setting, int? line, string id, params string[] arguments) =>
            issues.Add(new ConfigurationIssue(setting, line, new Message(id, [.. arguments])));
    }
}
