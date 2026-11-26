namespace Hephaestus.Core.Localization;

/// <summary>
/// Every message id. Each has an entry in <c>Messages.en.resx</c> (English) and <c>Messages.pl.resx</c> (Polish); a test fails
/// when one is missing from either, or when a resource entry has no id here.
/// </summary>
public static class MessageIds
{
    public const string VersionLine = "version.line";

    public const string CliRootDescription = "cli.root.description";
    public const string CliVersionDescription = "cli.version.description";
    public const string CliHelpUsage = "cli.help.usage";
    public const string CliHelpCommands = "cli.help.commands";
    public const string CliHelpOptions = "cli.help.options";
    public const string CliHelpPlaceholderCommand = "cli.help.placeholder-command";
    public const string CliHelpPlaceholderOptions = "cli.help.placeholder-options";
    public const string CliHelpOptionHelp = "cli.help.option-help";
    public const string CliHelpOptionJson = "cli.help.option-json";

    public const string CliNotUnderstoodTitle = "cli.not-understood.title";
    public const string CliNotUnderstoodUnmatched = "cli.not-understood.unmatched";
    public const string CliUnreachableTitle = "cli.unreachable.title";
    public const string CliUnreachableDetail = "cli.unreachable.detail";
    public const string CliBadAnswerTitle = "cli.bad-answer.title";
    public const string CliBadAnswerDetail = "cli.bad-answer.detail";

    public const string CliSecretDescription = "cli.secret.description";
    public const string CliSecretSetDescription = "cli.secret.set.description";
    public const string CliSecretListDescription = "cli.secret.list.description";
    public const string CliSecretRemoveDescription = "cli.secret.remove.description";
    public const string CliSecretSetDone = "cli.secret.set.done";
    public const string CliSecretRemoveDone = "cli.secret.remove.done";
    public const string CliSecretListHeader = "cli.secret.list.header";
    public const string CliSecretListEmpty = "cli.secret.list.empty";
    public const string CliSecretValueArgumentTitle = "cli.secret.value-argument.title";
    public const string CliSecretValueArgumentDetail = "cli.secret.value-argument.detail";

    public const string CliGitHubDescription = "cli.github.description";
    public const string CliGitHubConnectDescription = "cli.github.connect.description";
    public const string CliGitHubCheckDescription = "cli.github.check.description";
    public const string CliGitHubOptionAppId = "cli.github.option-app-id";
    public const string CliGitHubConnectArgumentsTitle = "cli.github.connect-arguments.title";
    public const string CliGitHubConnectArgumentsDetail = "cli.github.connect-arguments.detail";
    public const string CliGitHubConnected = "cli.github.connected";
    public const string CliGitHubApp = "cli.github.app";
    public const string CliGitHubInstallationsHeader = "cli.github.installations.header";
    public const string CliGitHubInstallationsNone = "cli.github.installations.none";
    public const string CliGitHubInstallation = "cli.github.installation";
    public const string CliGitHubSelectionAll = "cli.github.selection.all";
    public const string CliGitHubSelectionSelected = "cli.github.selection.selected";
    public const string CliGitHubInstallationSuspended = "cli.github.installation.suspended";
    public const string CliGitHubRepositoriesNone = "cli.github.repositories.none";
    public const string CliGitHubPermissionsHeader = "cli.github.permissions.header";
    public const string CliGitHubColumnPermission = "cli.github.column.permission";
    public const string CliGitHubColumnNeeds = "cli.github.column.needs";
    public const string CliGitHubColumnHas = "cli.github.column.has";
    public const string CliGitHubColumnStatus = "cli.github.column.status";
    public const string CliGitHubStatusPresent = "cli.github.status.present";
    public const string CliGitHubStatusMissing = "cli.github.status.missing";
    public const string CliGitHubWarning = "cli.github.warning";
    public const string CliGitHubReady = "cli.github.ready";
    public const string CliGitHubNotReady = "cli.github.not-ready";

    public const string ProblemNotFoundTitle = "problem.not-found.title";
    public const string ProblemNotFoundDetail = "problem.not-found.detail";
    public const string ProblemMethodNotAllowedTitle = "problem.method-not-allowed.title";
    public const string ProblemMethodNotAllowedDetail = "problem.method-not-allowed.detail";
    public const string ProblemUnexpectedTitle = "problem.unexpected.title";
    public const string ProblemUnexpectedDetail = "problem.unexpected.detail";
    public const string ProblemFailedTitle = "problem.failed.title";
    public const string ProblemFailedDetail = "problem.failed.detail";
    public const string ProblemSecretNameInvalidTitle = "problem.secret-name-invalid.title";
    public const string ProblemSecretNameInvalidDetail = "problem.secret-name-invalid.detail";
    public const string ProblemSecretValueEmptyTitle = "problem.secret-value-empty.title";
    public const string ProblemSecretValueEmptyDetail = "problem.secret-value-empty.detail";
    public const string ProblemSecretValueTooShortTitle = "problem.secret-value-too-short.title";
    public const string ProblemSecretValueTooShortDetail = "problem.secret-value-too-short.detail";
    public const string ProblemSecretValueTooLongTitle = "problem.secret-value-too-long.title";
    public const string ProblemSecretValueTooLongDetail = "problem.secret-value-too-long.detail";
    public const string ProblemSecretNotFoundTitle = "problem.secret-not-found.title";
    public const string ProblemSecretNotFoundDetail = "problem.secret-not-found.detail";
    public const string ProblemSecretValueNotTextTitle = "problem.secret-value-not-text.title";
    public const string ProblemSecretValueNotTextDetail = "problem.secret-value-not-text.detail";

    public const string ProblemGitHubNotConnectedTitle = "problem.github-not-connected.title";
    public const string ProblemGitHubNotConnectedDetail = "problem.github-not-connected.detail";
    public const string ProblemGitHubCredentialRejectedTitle = "problem.github-credential-rejected.title";
    public const string ProblemGitHubCredentialRejectedDetail = "problem.github-credential-rejected.detail";
    public const string ProblemGitHubKeyUnreadableDetail = "problem.github-key-unreadable.detail";
    public const string ProblemGitHubUnreachableTitle = "problem.github-unreachable.title";
    public const string ProblemGitHubUnreachableDetail = "problem.github-unreachable.detail";
    public const string ProblemGitHubAppIdInvalidTitle = "problem.github-app-id-invalid.title";
    public const string ProblemGitHubAppIdInvalidDetail = "problem.github-app-id-invalid.detail";

    public const string CliTrustedUserDescription = "cli.trusted-user.description";
    public const string CliTrustedUserAddDescription = "cli.trusted-user.add.description";
    public const string CliTrustedUserListDescription = "cli.trusted-user.list.description";
    public const string CliTrustedUserRemoveDescription = "cli.trusted-user.remove.description";
    public const string CliTrustedUserAdded = "cli.trusted-user.added";
    public const string CliTrustedUserAlready = "cli.trusted-user.already";
    public const string CliTrustedUserRemoved = "cli.trusted-user.removed";
    public const string CliTrustedUserListEmpty = "cli.trusted-user.list.empty";
    public const string CliTrustedUserColumnLogin = "cli.trusted-user.column.login";
    public const string CliTrustedUserColumnId = "cli.trusted-user.column.id";
    public const string CliTrustedUserColumnAdded = "cli.trusted-user.column.added";
    public const string ProblemTrustedUserLoginInvalidTitle = "problem.trusted-user-login-invalid.title";
    public const string ProblemTrustedUserLoginInvalidDetail = "problem.trusted-user-login-invalid.detail";
    public const string ProblemTrustedUserGithubNotFoundTitle = "problem.trusted-user-github-not-found.title";
    public const string ProblemTrustedUserGithubNotFoundDetail = "problem.trusted-user-github-not-found.detail";
    public const string ProblemTrustedUserOwnIdentityTitle = "problem.trusted-user-own-identity.title";
    public const string ProblemTrustedUserOwnIdentityDetail = "problem.trusted-user-own-identity.detail";
    public const string ProblemTrustedUserNotFoundTitle = "problem.trusted-user-not-found.title";
    public const string ProblemTrustedUserNotFoundDetail = "problem.trusted-user-not-found.detail";
}
