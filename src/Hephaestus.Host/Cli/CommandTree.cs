using System.CommandLine;
using Hephaestus.Core.Localization;

namespace Hephaestus.Host.Cli;

/// <summary>
/// The command tree (grammar and names: <c>EXPERIENCE.md</c>, "Command Line"). Command names and options are never
/// translated; each description is a message id resolved in the installation's language.
/// </summary>
internal sealed class CommandTree
{
    public CommandTree(Language language)
    {
        var catalog = MessageCatalog.Instance;

        Help = new Option<bool>("--help", "-h") { Description = catalog.Resolve(MessageIds.CliHelpOptionHelp, language), Recursive = true };
        Json = new Option<bool>("--json") { Description = catalog.Resolve(MessageIds.CliHelpOptionJson, language) };

        Version = new Command("version", catalog.Resolve(MessageIds.CliVersionDescription, language)) { Json };
        Root = new Command("hephaestus", catalog.Resolve(MessageIds.CliRootDescription, language)) { Version };
        Root.Options.Add(Help);
        Root.SetAction(_ => 0);
    }

    public Command Root { get; }

    public Command Version { get; }

    public Option<bool> Help { get; }

    public Option<bool> Json { get; }
}
