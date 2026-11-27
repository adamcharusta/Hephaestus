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

        AliasAddName = new Argument<string>("name");
        AliasRemoveName = new Argument<string>("name");
        AliasAgent = new Option<string>("--agent") { Description = Describe(MessageIds.CliAliasOptionAgent) };
        AliasModel = new Option<string>("--model") { Description = Describe(MessageIds.CliAliasOptionModel) };
        AliasEffort = new Option<string>("--effort") { Description = Describe(MessageIds.CliAliasOptionEffort) };
        AliasAdd = new Command("add", Describe(MessageIds.CliAliasAddDescription)) { AliasAddName, AliasAgent, AliasModel, AliasEffort, Json };
        AliasList = new Command("list", Describe(MessageIds.CliAliasListDescription)) { Json };
        AliasRemove = new Command("remove", Describe(MessageIds.CliAliasRemoveDescription)) { AliasRemoveName, Json };
        Alias = new Command("alias", Describe(MessageIds.CliAliasDescription)) { AliasAdd, AliasList, AliasRemove };

        Alias.SetAction(_ => 0);

        LimitTaskDuration = new Option<string>("--task-duration") { Description = Describe(MessageIds.CliLimitOptionTaskDuration) };
        LimitCpus = new Option<string>("--cpus") { Description = Describe(MessageIds.CliLimitOptionCpus) };
        LimitMemory = new Option<string>("--memory") { Description = Describe(MessageIds.CliLimitOptionMemory) };
        LimitSet = new Command("set", Describe(MessageIds.CliLimitSetDescription)) { LimitTaskDuration, LimitCpus, LimitMemory, Json };
        LimitShow = new Command("show", Describe(MessageIds.CliLimitShowDescription)) { Json };
        Limit = new Command("limit", Describe(MessageIds.CliLimitDescription)) { LimitSet, LimitShow };

        Limit.SetAction(_ => 0);

        ProtectedPathAddGlob = new Argument<string>("glob");
        ProtectedPathRemoveGlob = new Argument<string>("glob");
        ProtectedPathAdd = new Command("add", Describe(MessageIds.CliProtectedPathAddDescription)) { ProtectedPathAddGlob, Json };
        ProtectedPathList = new Command("list", Describe(MessageIds.CliProtectedPathListDescription)) { Json };
        ProtectedPathRemove = new Command("remove", Describe(MessageIds.CliProtectedPathRemoveDescription)) { ProtectedPathRemoveGlob, Json };
        ProtectedPath = new Command("protected-path", Describe(MessageIds.CliProtectedPathDescription)) { ProtectedPathAdd, ProtectedPathList, ProtectedPathRemove };

        ProtectedPath.SetAction(_ => 0);

        SettingSetName = new Argument<string>("name");
        SettingSetValue = new Argument<string>("value");
        SettingList = new Command("list", Describe(MessageIds.CliSettingListDescription)) { Json };
        SettingSet = new Command("set", Describe(MessageIds.CliSettingSetDescription)) { SettingSetName, SettingSetValue, Json };
        Setting = new Command("setting", Describe(MessageIds.CliSettingDescription)) { SettingList, SettingSet };

        Setting.SetAction(_ => 0);

        ConfigShow = new Command("show", Describe(MessageIds.CliConfigShowDescription)) { Json };
        Config = new Command("config", Describe(MessageIds.CliConfigDescription)) { ConfigShow };

        Config.SetAction(_ => 0);

        ProjectAddName = new Argument<string>("owner/repo");
        ProjectRemoveName = new Argument<string>("owner/repo");
        ProjectBaseBranch = new Option<string>("--base-branch") { Description = Describe(MessageIds.CliProjectOptionBaseBranch), Required = true };
        ProjectAll = new Option<bool>("--all") { Description = Describe(MessageIds.CliProjectOptionAll) };
        ProjectAdd = new Command("add", Describe(MessageIds.CliProjectAddDescription)) { ProjectAddName, ProjectBaseBranch, Json };
        ProjectList = new Command("list", Describe(MessageIds.CliProjectListDescription)) { ProjectAll, Json };
        ProjectRemove = new Command("remove", Describe(MessageIds.CliProjectRemoveDescription)) { ProjectRemoveName, Json };
        ProjectCheckName = new Argument<string>("owner/repo");
        ProjectCheck = new Command("check", Describe(MessageIds.CliProjectCheckDescription)) { ProjectCheckName, Json };
        Project = new Command("project", Describe(MessageIds.CliProjectDescription)) { ProjectAdd, ProjectList, ProjectRemove, ProjectCheck };

        Project.SetAction(_ => 0);

        Root = new Command("hephaestus", Describe(MessageIds.CliRootDescription)) { Version, Secret, GitHub, TrustedUser, Alias, Limit, ProtectedPath, Setting, Config, Project };
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

    public Command Alias { get; }

    public Command AliasAdd { get; }

    public Command AliasList { get; }

    public Command AliasRemove { get; }

    public Argument<string> AliasAddName { get; }

    public Argument<string> AliasRemoveName { get; }

    public Option<string> AliasAgent { get; }

    public Option<string> AliasModel { get; }

    public Option<string> AliasEffort { get; }

    public Command Limit { get; }

    public Command LimitSet { get; }

    public Command LimitShow { get; }

    public Option<string> LimitTaskDuration { get; }

    public Option<string> LimitCpus { get; }

    public Option<string> LimitMemory { get; }

    public Command ProtectedPath { get; }

    public Command ProtectedPathAdd { get; }

    public Command ProtectedPathList { get; }

    public Command ProtectedPathRemove { get; }

    public Argument<string> ProtectedPathAddGlob { get; }

    public Argument<string> ProtectedPathRemoveGlob { get; }

    public Command Setting { get; }

    public Command SettingList { get; }

    public Command SettingSet { get; }

    public Argument<string> SettingSetName { get; }

    public Argument<string> SettingSetValue { get; }

    public Command Project { get; }

    public Command ProjectAdd { get; }

    public Command ProjectList { get; }

    public Command ProjectRemove { get; }

    public Command ProjectCheck { get; }

    public Argument<string> ProjectCheckName { get; }

    public Argument<string> ProjectAddName { get; }

    public Argument<string> ProjectRemoveName { get; }

    public Option<string> ProjectBaseBranch { get; }

    public Option<bool> ProjectAll { get; }

    public Command Config { get; }

    public Command ConfigShow { get; }

    public Option<long> GitHubAppId { get; }

    public Argument<string> SecretSetName { get; }

    public Argument<string> SecretRemoveName { get; }

    public Option<bool> Help { get; }

    public Option<bool> Json { get; }
}
