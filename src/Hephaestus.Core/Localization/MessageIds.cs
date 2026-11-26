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
}
