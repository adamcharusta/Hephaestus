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

        // The App's private key is read from standard input like a secret's value, and is never an argument (UX "Command Line").
        GitHubAppId = new Option<long>("--app-id") { Description = Describe(MessageIds.CliGitHubOptionAppId), Required = true };
        GitHubConnect = new Command("connect", Describe(MessageIds.CliGitHubConnectDescription)) { GitHubAppId, Json };
        GitHubCheck = new Command("check", Describe(MessageIds.CliGitHubCheckDescription)) { Json };
        GitHub = new Command("github", Describe(MessageIds.CliGitHubDescription)) { GitHubConnect, GitHubCheck };

        GitHub.SetAction(_ => 0);

        TrustedUserAddLogin = new Argument<string>("login");
        TrustedUserRemoveLogin = new Argument<string>("login");
        TrustedUserAdd = new Command("add", Describe(MessageIds.CliTrustedUserAddDescription)) { TrustedUserAddLogin, Json };
        TrustedUserList = new Command("list", Describe(MessageIds.CliTrustedUserListDescription)) { Json };
        TrustedUserRemove = new Command("remove", Describe(MessageIds.CliTrustedUserRemoveDescription)) { TrustedUserRemoveLogin, Json };
        TrustedUser = new Command("trusted-user", Describe(MessageIds.CliTrustedUserDescription)) { TrustedUserAdd, TrustedUserList, TrustedUserRemove };

        TrustedUser.SetAction(_ => 0);

        Root = new Command("hephaestus", Describe(MessageIds.CliRootDescription)) { Version, Secret, GitHub, TrustedUser };
        Root.Options.Add(Help);
        Root.SetAction(_ => 0);
    }

    public Command Root { get; }

    public Command Version { get; }

    public Command Secret { get; }

    public Command SecretSet { get; }

    public Command SecretList { get; }

    public Command SecretRemove { get; }

    public Command GitHub { get; }

    public Command GitHubConnect { get; }

    public Command GitHubCheck { get; }

    public Command TrustedUser { get; }

    public Command TrustedUserAdd { get; }

    public Command TrustedUserList { get; }

    public Command TrustedUserRemove { get; }

    public Argument<string> TrustedUserAddLogin { get; }

    public Argument<string> TrustedUserRemoveLogin { get; }

    public Option<long> GitHubAppId { get; }

    public Argument<string> SecretSetName { get; }

    public Argument<string> SecretRemoveName { get; }

    public Option<bool> Help { get; }

    public Option<bool> Json { get; }
}
