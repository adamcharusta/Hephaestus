using System.CommandLine;
using Hephaestus.Core.Localization;

namespace Hephaestus.Host.Cli;

/// <summary>
/// The command tree (grammar and names: <c>EXPERIENCE.md</c>, "Command Line"). Command names, arguments, and options are never
/// translated; each description is a message id resolved in the installation's language.
/// </summary>
internal sealed class CommandTree
{
    public CommandTree(Language language)
    {
        var catalog = MessageCatalog.Instance;
        string Describe(string id) => catalog.Resolve(id, language);

        Help = new Option<bool>("--help", "-h") { Description = Describe(MessageIds.CliHelpOptionHelp), Recursive = true };
        Json = new Option<bool>("--json") { Description = Describe(MessageIds.CliHelpOptionJson) };

        Version = new Command("version", Describe(MessageIds.CliVersionDescription)) { Json };

        // A secret's value is read from standard input and is never an argument or an option (UX "Command Line").
        SecretSetName = new Argument<string>("name");
        SecretRemoveName = new Argument<string>("name");
        SecretSet = new Command("set", Describe(MessageIds.CliSecretSetDescription)) { SecretSetName, Json };
        SecretList = new Command("list", Describe(MessageIds.CliSecretListDescription)) { Json };
        SecretRemove = new Command("remove", Describe(MessageIds.CliSecretRemoveDescription)) { SecretRemoveName, Json };
        Secret = new Command("secret", Describe(MessageIds.CliSecretDescription)) { SecretSet, SecretList, SecretRemove };

        Secret.SetAction(_ => 0);

        Root = new Command("hephaestus", Describe(MessageIds.CliRootDescription)) { Version, Secret };
        Root.Options.Add(Help);
        Root.SetAction(_ => 0);
    }

    public Command Root { get; }

    public Command Version { get; }

    public Command Secret { get; }

    public Command SecretSet { get; }

    public Command SecretList { get; }

    public Command SecretRemove { get; }

    public Argument<string> SecretSetName { get; }

    public Argument<string> SecretRemoveName { get; }

    public Option<bool> Help { get; }

    public Option<bool> Json { get; }
}
