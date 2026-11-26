using Hephaestus.Core.Localization;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>Grammar and output conventions of <c>EXPERIENCE.md</c> that hold for every command: help, usage errors, exit codes.</summary>
public class CommandLineTests
{
    private static async Task<CliRun> RunAsync(Language language, params string[] args)
    {
        // No host runs: none of these commands may need one.
        using var socket = new TempSocket();
        return await CliRun.ExecuteAsync(socket.Path, language, args);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task Help_lists_the_commands_and_the_options_and_exits_0(string flag)
    {
        var run = await RunAsync(Language.English, flag);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        Assert.StartsWith("Usage: hephaestus <command> [options]", run.Output, StringComparison.Ordinal);
        Assert.Contains("Commands:", run.Output, StringComparison.Ordinal);
        Assert.Contains("  version       Prints the Hephaestus version.", run.Output, StringComparison.Ordinal);
        Assert.Contains("-h, --help", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_command_without_arguments_is_the_help_of_the_whole_tool()
    {
        var bare = await RunAsync(Language.English);
        var help = await RunAsync(Language.English, "--help");

        Assert.Equal(help, bare);
    }

    [Fact]
    public async Task The_server_entry_is_not_a_public_command_and_is_not_listed()
    {
        var run = await RunAsync(Language.English, "--help");
        var unknown = await RunAsync(Language.English, "serve", "now");

        Assert.DoesNotContain("serve", run.Output, StringComparison.Ordinal);
        Assert.Equal(2, unknown.ExitCode);
    }

    [Theory]
    [InlineData("version", "--help")]
    [InlineData("version", "-h")]
    [InlineData("version", "--json", "--help")]
    public async Task A_command_has_its_own_help_with_its_options(params string[] args)
    {
        var run = await RunAsync(Language.English, args);

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("Usage: hephaestus version [options]", run.Output, StringComparison.Ordinal);
        Assert.Contains("Prints the Hephaestus version.", run.Output, StringComparison.Ordinal);
        Assert.Contains("--json", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Commands:", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_in_Polish_has_no_English_sentence_and_the_same_commands_and_options()
    {
        var english = await RunAsync(Language.English, "version", "--help");
        var polish = await RunAsync(Language.Polish, "version", "--help");

        Assert.Equal(0, polish.ExitCode);
        Assert.StartsWith("Użycie: hephaestus version [opcje]", polish.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Usage", polish.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Options:", polish.Output, StringComparison.Ordinal);
        Assert.Contains("-h, --help", polish.Output, StringComparison.Ordinal);
        Assert.Contains("--json", polish.Output, StringComparison.Ordinal);
        Assert.NotEqual(english.Output, polish.Output);
    }

    [Theory]
    [InlineData(Language.English, "Command not understood", "Unrecognized command or argument 'frobnicate'. Run 'hephaestus --help' to list the commands and options.")]
    [InlineData(Language.Polish, "Polecenie nie zostało zrozumiane", "Nierozpoznane polecenie lub argument 'frobnicate'. Uruchom 'hephaestus --help', aby zobaczyć polecenia i opcje.")]
    public async Task An_unknown_command_is_exit_code_2_with_a_title_then_a_detail_on_standard_error(Language language, string title, string detail)
    {
        var run = await RunAsync(language, "frobnicate");

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal([title, detail], run.ErrorLines);
    }

    [Theory]
    [InlineData("version", "--frobnicate")]
    [InlineData("--json")]
    [InlineData("version", "extra")]
    public async Task An_unknown_option_or_argument_is_exit_code_2(params string[] args)
    {
        var run = await RunAsync(Language.English, args);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal("Command not understood", run.ErrorLines[0]);
    }
}
