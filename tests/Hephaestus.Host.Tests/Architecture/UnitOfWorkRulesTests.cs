using System.Reflection;
using System.Text.RegularExpressions;
using Hephaestus.Core.Loop;
using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>AD-1, Story 2.2: Task state is written only by the orchestrator loop, through <c>IUnitOfWork</c>, which nothing else may depend on.</summary>
public partial class UnitOfWorkRulesTests
{
    /// <summary>The port, the loop that uses it, the adapter that implements it, and the line that registers the adapter.</summary>
    private static readonly string[] Allowed =
    [
        "src/Hephaestus.Core/Ports/IUnitOfWork.cs",
        "src/Hephaestus.Core/Loop/OrchestratorLoop.cs",
        "src/Hephaestus.Persistence/SqliteUnitOfWork.cs",
        "src/Hephaestus.Persistence/PersistenceServiceCollectionExtensions.cs",
    ];

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>The file without its comment lines, which may talk about the port; only code can depend on it.</summary>
    private static string Code(string file) => string.Join('\n', File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string Relative(string file) => Path.GetRelativePath(RepositoryRoot.Path, file).Replace('\\', '/');

    [Fact]
    public void No_source_file_but_the_loop_and_the_adapter_that_implements_the_port_names_it()
    {
        var offenders = SourceFiles()
            .Where(file => !Allowed.Contains(Relative(file)))
            .Where(file => Names().IsMatch(Code(file)))
            .Select(Relative);

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_files_that_may_name_it_all_exist_so_that_a_rename_cannot_silently_widen_the_rule()
    {
        Assert.All(Allowed, file => Assert.True(File.Exists(Path.Combine(RepositoryRoot.Path, file)), file));
    }

    [Fact]
    public void No_type_but_the_loop_takes_the_port_in_a_constructor_a_member_or_a_method()
    {
        var assemblies = new[] { typeof(OrchestratorLoop).Assembly, typeof(Hephaestus.GitHub.GitHubPoller).Assembly, typeof(EntryPoint).Assembly, typeof(Hephaestus.Persistence.PersistenceOptions).Assembly };
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var offenders = new List<string>();
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        {
            var uses = type.GetConstructors(All).SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
                .Concat(type.GetFields(All).Select(f => f.FieldType))
                .Concat(type.GetProperties(All).Select(p => p.PropertyType))
                .Concat(type.GetMethods(All).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)));
            if (uses.Any(t => t == typeof(IUnitOfWork)) && type != typeof(OrchestratorLoop) && type != typeof(IUnitOfWork))
            {
                offenders.Add(type.FullName!);
            }
        }

        Assert.Empty(offenders);
        Assert.Contains(typeof(IUnitOfWork), typeof(OrchestratorLoop).GetConstructors().Single().GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void Nothing_in_Persistence_but_the_unit_of_work_writes_a_Task_or_its_snapshot()
    {
        var offenders = SourceFiles()
            .Where(file => Relative(file).StartsWith("src/Hephaestus.Persistence/", StringComparison.Ordinal))
            .Where(file => !Relative(file).StartsWith("src/Hephaestus.Persistence/Migrations/", StringComparison.Ordinal))
            .Where(file => Relative(file) != "src/Hephaestus.Persistence/SqliteUnitOfWork.cs")
            .Where(file => TaskWrites().IsMatch(Code(file)))
            .Select(Relative);

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("context.Tasks.Add(entry);")]
    [InlineData("context.IssueSnapshots.AddRange(rows);")]
    [InlineData("await context.Tasks.Where(t => t.Id == id).ExecuteUpdateAsync(set);")]
    [InlineData("context.Tasks.Remove(task);")]
    [InlineData("await context.Database.ExecuteSqlRawAsync(\"UPDATE \\\"Tasks\\\" SET 1\");")]
    [InlineData("INSERT INTO \"IssueSnapshots\" (\"TaskId\") VALUES (1)")]
    public void The_rule_catches_the_ways_Persistence_could_write_a_Task(string line)
    {
        Assert.Matches(TaskWrites(), line);
    }

    [Theory]
    [InlineData("return await context.Tasks.FirstOrDefaultAsync(t => t.Id == id);")]
    [InlineData("var snapshots = context.IssueSnapshots.Where(s => s.TaskId == id);")]
    public void The_rule_lets_Persistence_read_them(string line)
    {
        Assert.DoesNotMatch(TaskWrites(), line);
    }

    [Theory]
    [InlineData("private readonly IUnitOfWork _work;")]
    [InlineData("public Handler(IUnitOfWork work) { }")]
    [InlineData("services.AddSingleton<IUnitOfWork, Other>();")]
    [InlineData("await unitOfWork.ApplyAsync(x, token);")]
    [InlineData("var u = provider.GetRequiredService<IUnitOfWork>();")]
    public void The_rule_catches_the_ways_a_type_could_name_the_port(string line)
    {
        Assert.Matches(Names(), line);
    }

    [Theory]
    [InlineData("var unitsOfMeasure = 3;")]
    [InlineData("// a unit of work, in words")]
    public void The_rule_does_not_mistake_other_words_for_it(string line)
    {
        Assert.DoesNotMatch(Names(), line);
    }

    [GeneratedRegex(@"IUnitOfWork|(?i:\bunitofwork\b)")]
    private static partial Regex Names();

    [GeneratedRegex(@"\.(Tasks|IssueSnapshots)\b[^;]*?\.(Add\w*|Remove\w*|Update\w*|Attach\w*|ExecuteUpdate\w*|ExecuteDelete\w*)\(|(?i:(INSERT INTO|UPDATE|DELETE FROM)\s+\\?""?(Tasks|IssueSnapshots)\b)")]
    private static partial Regex TaskWrites();
}
