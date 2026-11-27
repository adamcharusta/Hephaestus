using System.Text;
using System.Text.RegularExpressions;
using Hephaestus.Core.Context;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;
using Hephaestus.Host.Hosting;
using Hephaestus.Host.Tests.Architecture;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>Story 3.11, AD-12, AD-14, AD-21: the <c>default</c> Methodology the image ships, as it is on disk, and what Core makes of it.</summary>
public sealed partial class DefaultMethodologyTests
{
    private static readonly string Directory = System.IO.Path.Combine(RepositoryRoot.Path, "methodologies");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<string> TemplateAsync() => await File.ReadAllTextAsync(System.IO.Path.Combine(Directory, "default", "implementation.md"), Cancel);

    [GeneratedRegex(@"/(?:hephaestus|workspace)(?:/[A-Za-z0-9._-]+)*")]
    private static partial Regex PathPattern();

    [Fact]
    public async Task The_default_Methodology_holds_a_manifest_and_the_Implementation_template()
    {
        var methodology = await new DirectoryMethodologyCatalog(Directory).ReadAsync("default", Cancel);

        Assert.Equal(["implementation.md", "manifest.yml"], methodology!.Files.Select(file => file.Path));
    }

    [Fact]
    public async Task The_template_refers_only_to_context_file_paths_and_the_repository()
    {
        var paths = PathPattern().Matches(await TemplateAsync()).Select(match => match.Value).Distinct().ToList();

        Assert.NotEmpty(paths);
        var allowed = ContextFiles.All.Select(name => StepContainerLayout.Context + "/" + name).Concat([StepContainerLayout.Context, StepContainerLayout.Workspace]).ToHashSet();
        Assert.All(paths, path => Assert.Contains(path, allowed));
        Assert.Contains(AgentPrompt.InstructionPath.Replace("instruction.md", "task.json", StringComparison.Ordinal), paths);
    }

    [Fact]
    public async Task The_template_has_no_substitution()
    {
        var template = await TemplateAsync();

        Assert.DoesNotMatch(@"\{\{|\}\}|\$\{|\{[A-Za-z_]+\}|<%|%>", template);
    }

    [Theory]
    [InlineData("CLAUDE.md")]
    [InlineData("AGENTS.md")]
    [InlineData("sub-agent")]
    [InlineData("subagent")]
    [InlineData("slash command")]
    [InlineData("/skill")]
    [InlineData("skill")]
    [InlineData("Claude")]
    [InlineData("Codex")]
    [InlineData("Anthropic")]
    [InlineData("OpenAI")]
    public async Task The_template_relies_on_no_skill_no_sub_agent_no_slash_command_no_instruction_file_and_names_no_Agent(string forbidden)
    {
        Assert.DoesNotContain(forbidden, await TemplateAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_template_tells_the_Agent_to_write_its_prose_in_the_language_of_task_json_without_naming_a_language_to_use()
    {
        var template = await TemplateAsync();

        Assert.Contains("language", template, StringComparison.Ordinal);
        Assert.Contains("task.json", template, StringComparison.Ordinal);
        Assert.Contains("result.schema.json", template, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_template_is_ASCII_free_of_control_characters_and_ends_with_a_newline()
    {
        var template = await TemplateAsync();

        Assert.EndsWith("\n", template, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", template, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_Methodology_named_gives_the_instruction_of_the_shipped_default_for_both_Agents_and_both_languages()
    {
        var catalog = new DirectoryMethodologyCatalog(Directory);
        var shipped = await File.ReadAllBytesAsync(System.IO.Path.Combine(Directory, "default", "implementation.md"), Cancel);
        StepContextInput Input(Language language) => new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, "hephaestus/1-x", "0123456789abcdef0123456789abcdef01234567", "main", StepKind.Implementation, 0, language);

        var english = await StepContextAssembler.AssembleAsync(catalog, Input(Language.English), Cancel);
        var polish = await StepContextAssembler.AssembleAsync(catalog, Input(Language.Polish), Cancel);

        Assert.Equal(shipped, english.Context.Single(file => file.Path == ContextFiles.Instruction).Content);
        Assert.Equal(shipped, polish.Context.Single(file => file.Path == ContextFiles.Instruction).Content);
        Assert.Equal("default", english.MethodologyName);
        Assert.Equal("implementation.md", english.Template.Path);
        Assert.Equal(Encoding.UTF8.GetString(shipped), Encoding.UTF8.GetString(english.Context[0].Content));
    }
}
