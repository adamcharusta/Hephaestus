using System.Reflection;
using System.Text.RegularExpressions;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Hephaestus.Host.Hosting;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Hephaestus.Host.Tests.Architecture;

/// <summary>AR-36, AD-16: one masker, defined in Core and built from <c>ISecretStore</c>, and the logger writes through it.</summary>
public partial class MaskerRulesTests
{
    private static readonly string[] SourceProjects =
    [
        "Hephaestus.Core",
        "Hephaestus.Agents",
        "Hephaestus.GitHub",
        "Hephaestus.Workspaces",
        "Hephaestus.Persistence",
        "Hephaestus.Host",
    ];

    [Fact]
    public void There_is_exactly_one_masker_and_it_is_defined_in_Core()
    {
        var maskers = SourceProjects
            .Select(Assembly.Load)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.Contains("Masker", StringComparison.Ordinal) && !type.IsNested)
            .ToList();

        var masker = Assert.Single(maskers);
        Assert.Equal(typeof(SecretMasker), masker);
        Assert.Equal(typeof(SecretMasker).Assembly, masker.Assembly);
    }

    [Fact]
    public void The_masker_is_built_from_the_secret_store_and_from_nothing_else()
    {
        var constructor = Assert.Single(typeof(SecretMasker).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        Assert.Equal([typeof(ISecretStore)], constructor.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public async Task The_host_registers_one_masker_and_its_logger_is_one_console_provider_whose_formatter_takes_the_masker()
    {
        await using var host = await RunningHost.StartAsync();
        var services = host.App.Services;

        Assert.NotNull(services.GetRequiredService<SecretMasker>());
        Assert.Same(services.GetRequiredService<SecretMasker>(), services.GetRequiredService<SecretMasker>());
        Assert.IsType<ConsoleLoggerProvider>(Assert.Single(services.GetServices<ILoggerProvider>()));
        Assert.Equal(MaskedJsonFormatter.FormatterName, services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName);
        // The framework registers its own formatters too; the console provider uses the one its options name.
        var formatter = Assert.Single(services.GetServices<ConsoleFormatter>(), f => f.Name == MaskedJsonFormatter.FormatterName);
        Assert.IsType<MaskedJsonFormatter>(formatter);
        Assert.Contains(typeof(MaskedJsonFormatter).GetConstructors().Single().GetParameters(), p => p.ParameterType == typeof(SecretMasker));
    }

    [Fact]
    public void No_source_file_adds_a_second_way_to_write_a_log()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => OtherLoggerWriters().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(RepositoryRoot.Path, file))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Only_the_masker_and_the_store_that_implements_the_port_read_every_value()
    {
        var readers = SourceFiles()
            .Where(file => File.ReadAllText(file).Contains("ReadAllAsync(", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file))
            .Order(StringComparer.Ordinal);

        Assert.Equal(["FileSecretStore.cs", "ISecretStore.cs", "SecretMasker.cs"], readers);
    }

    [Fact]
    public void Only_the_programs_entry_names_the_console_and_everything_else_writes_to_the_writers_it_is_given()
    {
        var offenders = SourceFiles()
            .Where(file => Path.GetFileName(file) != "Program.cs")
            .Where(file => ConsoleWriters().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(RepositoryRoot.Path, file));

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [GeneratedRegex(@"\bConsole\.(Out|Error|Write|WriteLine|OpenStandard)")]
    private static partial Regex ConsoleWriters();

    [GeneratedRegex(@"\.Add(Json|Simple|Systemd)Console\(|\.AddProvider\(|\.AddDebug\(|\.AddEventLog\(|\.AddEventSourceLogger\(|\.AddSystemdConsole\(")]
    private static partial Regex OtherLoggerWriters();
}
