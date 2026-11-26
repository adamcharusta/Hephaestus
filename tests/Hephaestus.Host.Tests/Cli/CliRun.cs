using Hephaestus.Core.Localization;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>One run of the command line: what it printed on each stream, and its exit code.</summary>
internal sealed record CliRun(int ExitCode, string Output, string Error)
{
    public static Task<CliRun> ExecuteAsync(string socketPath, Language language, params string[] args) =>
        ExecuteWithInputAsync(string.Empty, socketPath, language, args);

    /// <summary>A run whose standard input holds <paramref name="input"/>, as a pipe into <c>docker exec -i</c> does.</summary>
    public static async Task<CliRun> ExecuteWithInputAsync(string input, string socketPath, Language language, params string[] args)
    {
        var environment = new Dictionary<string, string?>
        {
            [HostSettings.ControlSocketVariable] = socketPath,
            [HostSettings.LanguageVariable] = language.Tag(),
        };

        using var stdin = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await EntryPoint.RunAsync(args, stdin, output, error, name => environment.GetValueOrDefault(name), TestContext.Current.CancellationToken);
        return new CliRun(exitCode, output.ToString(), error.ToString());
    }

    public string[] ErrorLines => Error.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
