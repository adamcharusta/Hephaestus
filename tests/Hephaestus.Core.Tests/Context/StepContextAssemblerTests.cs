using System.Text;
using System.Text.Json;
using Hephaestus.Core.Context;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Methodologies;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Context;

/// <summary>Story 3.11, AD-12, AD-14, AD-21, FR-61, FR-62, FR-69: one context file set per step run, with the instructions of the Methodology's template for the step kind and no more.</summary>
public class StepContextAssemblerTests
{
    private static readonly byte[] DefaultTemplate = Encoding.UTF8.GetBytes("Read /hephaestus/context/task.json.\n");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static StepContextInput Input(string? methodology = null, Language language = Language.English, StepKind kind = StepKind.Implementation) => new(
        Guid.Parse("0197a8a0-0000-7000-8000-000000000001"),
        Guid.Parse("0197a8a0-0000-7000-8000-000000000002"),
        101,
        7,
        "hephaestus/7-add-a-thing",
        "0123456789abcdef0123456789abcdef01234567",
        "main",
        kind,
        0,
        language,
        methodology);

    private static Methodology Default(byte[]? template = null) => new("default", [new WorkspaceFile("manifest.yml", "name: default\n"u8.ToArray()), new WorkspaceFile("implementation.md", template ?? DefaultTemplate)]);

    private static string Read(AssembledContext assembled, string name) => Encoding.UTF8.GetString(assembled.Context.Single(file => file.Path == name).Content);

    [Fact]
    public async Task The_instruction_is_the_template_unchanged_and_the_Methodology_is_named_with_the_identity_of_the_template()
    {
        var assembled = await StepContextAssembler.AssembleAsync(new Catalog(Default()), Input(), Cancel);

        Assert.Equal(DefaultTemplate, assembled.Context.Single(file => file.Path == ContextFiles.Instruction).Content);
        Assert.Equal("default", assembled.MethodologyName);
        Assert.Equal("implementation.md", assembled.Template.Path);
        Assert.Equal(64, assembled.Template.Hash.Length);
        Assert.Equal(assembled.Template.Hash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(DefaultTemplate)));
        Assert.Equal($"implementation.md@sha256:{assembled.Template.Hash}", assembled.Template.ToString());
        Assert.Equal(["manifest.yml", "implementation.md"], assembled.Methodology.Select(file => file.Path));
    }

    [Fact]
    public async Task An_edited_template_is_another_identity()
    {
        var before = await StepContextAssembler.AssembleAsync(new Catalog(Default()), Input(), Cancel);
        var after = await StepContextAssembler.AssembleAsync(new Catalog(Default("Read /hephaestus/context/task.json. Be careful.\n"u8.ToArray())), Input(), Cancel);

        Assert.NotEqual(before.Template.Hash, after.Template.Hash);
    }

    [Fact]
    public async Task No_Methodology_named_is_the_built_in_default_whether_the_name_is_null_or_blank()
    {
        var catalog = new Catalog(Default());

        foreach (var name in new string?[] { null, "", "  " })
        {
            var assembled = await StepContextAssembler.AssembleAsync(catalog, Input(name), Cancel);
            Assert.Equal("default", assembled.MethodologyName);
        }

        Assert.All(catalog.Reads, read => Assert.Equal("default", read));
    }

    [Fact]
    public async Task The_same_step_assembled_for_two_Agents_gives_byte_identical_files()
    {
        // Nothing in the input names an Agent: each Agent's adapter is handed the same assembled set (FR-62).
        var catalog = new Catalog(Default());
        var forClaudeCode = await StepContextAssembler.AssembleAsync(catalog, Input(), Cancel);
        var forCodex = await StepContextAssembler.AssembleAsync(catalog, Input(), Cancel);

        Assert.Equal(forClaudeCode.Context.Select(file => file.Path), forCodex.Context.Select(file => file.Path));
        foreach (var (left, right) in forClaudeCode.Context.Zip(forCodex.Context))
        {
            Assert.Equal(left.Content, right.Content);
        }

        Assert.Equal(forClaudeCode.Template, forCodex.Template);
        Assert.Equal(typeof(StepContextInput).GetProperties().Select(property => property.Name).Where(name => name.Contains("Agent", StringComparison.Ordinal)), []);
    }

    [Fact]
    public async Task The_instruction_is_the_same_in_every_language_and_for_every_Task_and_only_task_json_tells_them_apart()
    {
        var catalog = new Catalog(Default());
        var english = await StepContextAssembler.AssembleAsync(catalog, Input(language: Language.English), Cancel);
        var polish = await StepContextAssembler.AssembleAsync(catalog, Input(language: Language.Polish) with { TaskId = Guid.NewGuid(), Branch = "hephaestus/9-other" }, Cancel);

        Assert.Equal(english.Context.Single(file => file.Path == ContextFiles.Instruction).Content, polish.Context.Single(file => file.Path == ContextFiles.Instruction).Content);
        Assert.Equal("en", JsonDocument.Parse(Read(english, ContextFiles.Task)).RootElement.GetProperty("language").GetString());
        Assert.Equal("pl", JsonDocument.Parse(Read(polish, ContextFiles.Task)).RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Task_json_carries_the_ids_the_branch_the_start_commit_the_Base_Branch_the_step_kind_the_round_and_the_language()
    {
        var assembled = await StepContextAssembler.AssembleAsync(new Catalog(Default()), Input() with { Round = 2 }, Cancel);

        var task = JsonDocument.Parse(Read(assembled, ContextFiles.Task)).RootElement;
        Assert.Equal(
            ["taskId", "stepRunId", "repositoryId", "issueNumber", "branch", "startCommit", "baseBranch", "stepKind", "round", "language"],
            task.EnumerateObject().Select(property => property.Name));
        Assert.Equal("0197a8a0-0000-7000-8000-000000000001", task.GetProperty("taskId").GetString());
        Assert.Equal("0197a8a0-0000-7000-8000-000000000002", task.GetProperty("stepRunId").GetString());
        Assert.Equal(101, task.GetProperty("repositoryId").GetInt64());
        Assert.Equal(7, task.GetProperty("issueNumber").GetInt32());
        Assert.Equal("hephaestus/7-add-a-thing", task.GetProperty("branch").GetString());
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", task.GetProperty("startCommit").GetString());
        Assert.Equal("main", task.GetProperty("baseBranch").GetString());
        Assert.Equal("implementation", task.GetProperty("stepKind").GetString());
        Assert.Equal(2, task.GetProperty("round").GetInt32());
    }

    [Fact]
    public async Task An_input_that_does_not_exist_is_an_absent_file()
    {
        var assembled = await StepContextAssembler.AssembleAsync(new Catalog(Default()), Input(), Cancel);

        Assert.Equal([ContextFiles.Instruction, ContextFiles.Task, ContextFiles.ResultSchema], assembled.Context.Select(file => file.Path));
    }

    [Fact]
    public async Task Every_input_that_exists_has_the_file_of_its_fixed_name()
    {
        var input = Input() with
        {
            Issue = new IssueSnapshot("Add a thing", "Please add a thing.", "hash", 3, DateTimeOffset.UnixEpoch),
            Comments = [new AdmittedComment(Guid.NewGuid(), 55, 9, "maria", "Use the old name.", "h", 4, DateTimeOffset.UnixEpoch)],
            Plan = "# Plan\n",
            Findings = "[]",
            Validation = "{}",
            Handoff = "Earlier steps did X.\n",
        };

        var assembled = await StepContextAssembler.AssembleAsync(new Catalog(Default()), input, Cancel);

        Assert.Equal(ContextFiles.All, assembled.Context.Select(file => file.Path));
        Assert.Equal("# Add a thing\n\nPlease add a thing.\n", Read(assembled, ContextFiles.Issue));
        var comment = JsonDocument.Parse(Read(assembled, ContextFiles.Comments)).RootElement.EnumerateArray().Single();
        Assert.Equal("Use the old name.", comment.GetProperty("body").GetString());
        Assert.Equal("maria", comment.GetProperty("authorLogin").GetString());
        Assert.Equal("# Plan\n", Read(assembled, ContextFiles.Plan));
        Assert.Equal("[]", Read(assembled, ContextFiles.Findings));
        Assert.Equal("{}", Read(assembled, ContextFiles.Validation));
        Assert.Equal("Earlier steps did X.\n", Read(assembled, ContextFiles.Handoff));
    }

    [Fact]
    public async Task A_comment_file_holds_only_what_the_admitted_comment_carries()
    {
        var input = Input() with
        {
            Comments = [new AdmittedComment(Guid.NewGuid(), 55, 9, "maria", "Body", "the-hash", 4, DateTimeOffset.UnixEpoch)],
        };

        var assembled = await StepContextAssembler.AssembleAsync(new Catalog(Default()), input, Cancel);

        var element = JsonDocument.Parse(Read(assembled, ContextFiles.Comments)).RootElement.EnumerateArray().Single();
        Assert.Equal(["commentId", "authorLogin", "body", "answersEscalationId", "admittedAt"], element.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task The_result_schema_of_the_kind_is_written_and_returned()
    {
        var assembled = await StepContextAssembler.AssembleAsync(new Catalog(Default()), Input(), Cancel);

        Assert.NotNull(assembled.ResultSchema);
        Assert.Equal(assembled.ResultSchema + "\n", Read(assembled, ContextFiles.ResultSchema));
    }

    [Fact]
    public async Task A_Methodology_that_is_not_shipped_is_refused_with_its_name()
    {
        var exception = await Assert.ThrowsAsync<MethodologyException>(() => StepContextAssembler.AssembleAsync(new Catalog(Default()), Input("bmad"), Cancel));

        Assert.Equal(MessageIds.MethodologyUnavailable, exception.Reason.Id);
        Assert.Equal(["bmad"], exception.Reason.Arguments.Select(argument => argument.ToString()));
    }

    [Fact]
    public async Task A_Methodology_without_a_template_for_the_kind_is_refused_with_the_kind()
    {
        var exception = await Assert.ThrowsAsync<MethodologyException>(() => StepContextAssembler.AssembleAsync(new Catalog(Default()), Input(kind: StepKind.Review), Cancel));

        Assert.Equal(MessageIds.MethodologyTemplateMissing, exception.Reason.Id);
        Assert.Equal(["default", "review"], exception.Reason.Arguments.Select(argument => argument.ToString()));
    }

    [Fact]
    public async Task The_template_is_selected_by_the_step_kind_alone_for_any_Methodology_name()
    {
        var other = new Methodology("something-else", [new WorkspaceFile("implementation.md", "Another.\n"u8.ToArray())]);

        var assembled = await StepContextAssembler.AssembleAsync(new Catalog(other), Input("something-else"), Cancel);

        Assert.Equal("Another.\n", Read(assembled, ContextFiles.Instruction));
        Assert.Equal("something-else", assembled.MethodologyName);
    }

    [Fact]
    public void The_fixed_names_are_the_ones_of_the_architecture()
    {
        Assert.Equal(
            ["instruction.md", "task.json", "issue.md", "comments.json", "plan.md", "findings.json", "validation.json", "handoff.md", "result.schema.json"],
            ContextFiles.All);
    }

    [Fact]
    public async Task Null_arguments_are_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => StepContextAssembler.AssembleAsync(null!, Input(), Cancel));
        await Assert.ThrowsAsync<ArgumentNullException>(() => StepContextAssembler.AssembleAsync(new Catalog(Default()), null!, Cancel));
        Assert.Throws<ArgumentNullException>(() => MethodologyTemplates.Select(null!, StepKind.Implementation));
    }

    private sealed class Catalog(Methodology methodology) : IMethodologyCatalog
    {
        public List<string> Reads { get; } = [];

        public IReadOnlyList<string> ListNames() => [methodology.Name];

        public Task<Methodology?> ReadAsync(string name, CancellationToken cancellationToken)
        {
            Reads.Add(name);
            return Task.FromResult<Methodology?>(name == methodology.Name ? methodology : null);
        }
    }
}
