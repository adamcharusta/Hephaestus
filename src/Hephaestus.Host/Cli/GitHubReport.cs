using Hephaestus.Core.Localization;
using Hephaestus.Host.Api;

namespace Hephaestus.Host.Cli;

/// <summary>
/// Writes the identity of Hephaestus's GitHub App for a person (UX "Command Line", Output): sentences and table headers in the
/// installation's language; permission names, levels, logins, and repository names as they are.
/// </summary>
internal static class GitHubReport
{
    public static void Write(GitHubIdentityDocument identity, bool connected, MessageCatalog catalog, Language language, TextWriter output)
    {
        string Say(string id) => catalog.Resolve(id, language);
        string SayMessage(string id, params object[] arguments) => catalog.Resolve(new Message(id, arguments), language);

        if (connected)
        {
            output.WriteLine(Say(MessageIds.CliGitHubConnected));
        }

        output.WriteLine(SayMessage(MessageIds.CliGitHubApp, identity.Name, identity.Slug, identity.AppId, identity.Owner));

        output.WriteLine();
        if (identity.Installations.Count == 0)
        {
            output.WriteLine(Say(MessageIds.CliGitHubInstallationsNone));
        }
        else
        {
            output.WriteLine(Say(MessageIds.CliGitHubInstallationsHeader));
            foreach (var installation in identity.Installations)
            {
                var reach = installation.Suspended
                    ? Say(MessageIds.CliGitHubInstallationSuspended)
                    : installation.RepositorySelection switch
                    {
                        "all" => Say(MessageIds.CliGitHubSelectionAll),
                        "selected" => Say(MessageIds.CliGitHubSelectionSelected),
                        var other => other,
                    };
                output.WriteLine("  " + SayMessage(MessageIds.CliGitHubInstallation, installation.Account, installation.AccountType, reach));
                foreach (var repository in installation.Repositories)
                {
                    output.WriteLine("    " + repository);
                }

                if (!installation.Suspended && installation.Repositories.Count == 0)
                {
                    output.WriteLine("    " + Say(MessageIds.CliGitHubRepositoriesNone));
                }
            }
        }

        output.WriteLine();
        output.WriteLine(Say(MessageIds.CliGitHubPermissionsHeader));
        var rows = new List<string[]>
        {
            new[] { Say(MessageIds.CliGitHubColumnPermission), Say(MessageIds.CliGitHubColumnNeeds), Say(MessageIds.CliGitHubColumnHas), Say(MessageIds.CliGitHubColumnStatus) },
        };
        rows.AddRange(identity.Permissions.Select(p => new[]
        {
            p.Name,
            p.Required,
            p.Granted,
            Say(p.Present ? MessageIds.CliGitHubStatusPresent : MessageIds.CliGitHubStatusMissing),
        }));
        var widths = Enumerable.Range(0, 4).Select(column => rows.Max(row => row[column].Length)).ToArray();
        foreach (var row in rows)
        {
            output.WriteLine("  " + string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd());
        }

        foreach (var warning in identity.Warnings)
        {
            output.WriteLine();
            output.WriteLine(SayMessage(MessageIds.CliGitHubWarning, warning.Name, warning.Granted));
        }

        output.WriteLine();
        output.WriteLine(Say(identity.Ready ? MessageIds.CliGitHubReady : MessageIds.CliGitHubNotReady));
    }
}
