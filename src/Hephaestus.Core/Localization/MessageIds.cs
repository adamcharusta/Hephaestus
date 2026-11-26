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

    public const string ProblemNotFoundTitle = "problem.not-found.title";
    public const string ProblemNotFoundDetail = "problem.not-found.detail";
    public const string ProblemMethodNotAllowedTitle = "problem.method-not-allowed.title";
    public const string ProblemMethodNotAllowedDetail = "problem.method-not-allowed.detail";
    public const string ProblemUnexpectedTitle = "problem.unexpected.title";
    public const string ProblemUnexpectedDetail = "problem.unexpected.detail";
    public const string ProblemFailedTitle = "problem.failed.title";
    public const string ProblemFailedDetail = "problem.failed.detail";
}
