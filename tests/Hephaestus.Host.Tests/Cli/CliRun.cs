using Hephaestus.Core.Localization;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>One run of the command line: what it printed on each stream, and its exit code.</summary>
internal sealed record CliRun(int ExitCode, string Output, string Error)
{
    public static async Task<CliRun> ExecuteAsync(string socketPath, Language language, params string[] args)
    {
        var environment = new Dictionary<string, string?>
        {
            [HostSettings.ControlSocketVariable] = socketPath,
            [HostSettings.LanguageVariable] = language.Tag(),
        };

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await EntryPoint.RunAsync(args, output, error, name => environment.GetValueOrDefault(name), TestContext.Current.CancellationToken);
        return new CliRun(exitCode, output.ToString(), error.ToString());
    }

    public string[] ErrorLines => Error.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
