using System.CommandLine;
using Hephaestus.Core.Localization;

namespace Hephaestus.Host.Cli;

/// <summary>Prints the help of a command from message ids, so no sentence of it is the parser library's (AD-21).</summary>
internal static class HelpWriter
{
    public static void Write(Command command, Option helpOption, TextWriter output, Language language)
    {
        var catalog = MessageCatalog.Instance;
        var words = new List<string>();
        for (var c = command; c is not null; c = c.Parents.OfType<Command>().FirstOrDefault())
        {
            words.Insert(0, c.Name);
        }

        var subcommands = command.Subcommands.Where(s => !s.Hidden).ToList();
        var usage = string.Join(' ', words)
            + string.Concat(command.Arguments.Select(a => $" <{a.Name}>"))
            + (subcommands.Count > 0 ? " " + catalog.Resolve(MessageIds.CliHelpPlaceholderCommand, language) : string.Empty)
            + " " + catalog.Resolve(MessageIds.CliHelpPlaceholderOptions, language);

        output.WriteLine(catalog.Resolve(new Message(MessageIds.CliHelpUsage, usage), language));
        if (!string.IsNullOrEmpty(command.Description))
        {
            output.WriteLine();
            output.WriteLine(command.Description);
        }

        if (subcommands.Count > 0)
        {
            output.WriteLine();
            output.WriteLine(catalog.Resolve(MessageIds.CliHelpCommands, language));
            WriteRows(output, subcommands.Select(s => (s.Name, s.Description ?? string.Empty)));
        }

        output.WriteLine();
        output.WriteLine(catalog.Resolve(MessageIds.CliHelpOptions, language));
        WriteRows(output, command.Options.Append(helpOption).Distinct().Where(o => !o.Hidden).OrderBy(o => o == helpOption ? 0 : 1).Select(o => (string.Join(", ", o.Aliases.Prepend(o.Name).Distinct().OrderBy(n => n.Length == 2 ? 0 : 1)), o.Description ?? string.Empty)));
    }

    private static void WriteRows(TextWriter output, IEnumerable<(string Name, string Description)> rows)
    {
        var table = rows.ToList();
        var width = table.Max(r => r.Name.Length);
        foreach (var (name, description) in table)
        {
            output.WriteLine($"  {name.PadRight(width)}  {description}");
        }
    }
}
