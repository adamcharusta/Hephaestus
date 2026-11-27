using System.Text;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Projects;

namespace Hephaestus.Core.Tests.Projects;

/// <summary>Story 1.11, FR-2, FR-3, FR-5, FR-7, FR-31, FR-61, FR-69, AD-16: the bytes of <c>hephaestus.yml</c> are parsed and validated in Core, and every problem is reported.</summary>
public class ProjectConfigurationParserTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static readonly Alias[] Aliases =
    [
        Alias.ForModel("opus5.5", Agent.ClaudeCode, "claude-opus-5-5", Now),
        Alias.ForModel("sonnet5.5", Agent.ClaudeCode, "claude-sonnet-5-5", Now),
        Alias.ForModel("sol6.2", Agent.CodexCli, "gpt-6.2", Now),
        Alias.ForEffort("medium", Effort.Medium, Now),
    ];

    private const string Minimal = "image: node:22\nvalidation:\n  - npm test\n";

    private static ConfigurationContext Context(InstallationLimits? limits = null, params string[] methodologies) =>
        new(Aliases, limits ?? InstallationLimits.None, methodologies.Length == 0 ? ["default"] : methodologies);

    private static ConfigurationParse Parse(string yaml, ConfigurationContext? context = null) =>
        ProjectConfigurationParser.Parse(Encoding.UTF8.GetBytes(yaml), context ?? Context());

    private static ProjectConfiguration Valid(string yaml, ConfigurationContext? context = null)
    {
        var parse = Parse(yaml, context);
        Assert.True(parse.IsValid, Describe(parse));
        Assert.Empty(parse.Issues);
        return parse.Configuration!;
    }

    private static string Describe(ConfigurationParse parse) => string.Join("; ", parse.Issues.Select(i => $"{i.Setting}@{i.Line}: {i.Reason.Id}({string.Join(",", i.Reason.Arguments)})"));

    private static ConfigurationIssue Single(string yaml, ConfigurationContext? context = null)
    {
        var parse = Parse(yaml, context);
        Assert.False(parse.IsValid);
        Assert.Null(parse.Configuration);
        return Assert.Single(parse.Issues);
    }

    private static void AssertIssue(ConfigurationIssue issue, string setting, string id)
    {
        Assert.Equal(setting, issue.Setting);
        Assert.Equal(id, issue.Reason.Id);
    }

    [Fact]
    public void A_file_with_a_command_and_an_image_is_valid_and_takes_the_defaults_that_need_no_installation()
    {
        var configuration = Valid(Minimal);

        Assert.Null(configuration.PrimaryAlias);
        Assert.Null(configuration.FallbackAlias);
        Assert.Null(configuration.Effort);
        Assert.Equal("default", configuration.Methodology);
        Assert.Equal(["npm test"], configuration.Validation);
        Assert.Equal(2, configuration.CorrectionRounds);
        Assert.Equal("node:22", configuration.Image);
        Assert.Equal(ProjectLimits.None, configuration.Limits);
        Assert.Empty(configuration.AllowedEgressHosts);
        Assert.Null(configuration.Language);
    }

    [Fact]
    public void A_file_that_sets_every_key_is_valid_and_each_value_is_read()
    {
        var configuration = Valid("""
            primaryAlias: opus5.5
            fallbackAlias: sol6.2
            effort: medium
            methodology: default
            validation:
              - dotnet build
              - dotnet test
            correctionRounds: 3
            image: mcr.microsoft.com/dotnet/sdk:10.0
            limits:
              taskDuration: 90m
              cpus: 1.5
              memory: 4g
            allowedEgressHosts:
              - api.nuget.org
              - Registry.NPMJS.org
              - api.nuget.org
            language: pl
            """);

        Assert.Equal(("opus5.5", "sol6.2", "medium", "default"), (configuration.PrimaryAlias, configuration.FallbackAlias, configuration.Effort, configuration.Methodology));
        Assert.Equal(["dotnet build", "dotnet test"], configuration.Validation);
        Assert.Equal(3, configuration.CorrectionRounds);
        Assert.Equal("mcr.microsoft.com/dotnet/sdk:10.0", configuration.Image);
        Assert.Equal(new ProjectLimits(TimeSpan.FromMinutes(90), 1500, 4L * 1024 * 1024 * 1024), configuration.Limits);
        Assert.Equal(["api.nuget.org", "registry.npmjs.org"], configuration.AllowedEgressHosts);
        Assert.Equal(Language.Polish, configuration.Language);
    }

    [Fact]
    public void A_byte_order_mark_and_comments_are_allowed()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("# the build\n" + Minimal)).ToArray();

        Assert.True(ProjectConfigurationParser.Parse(bytes, Context()).IsValid);
    }

    [Fact]
    public void Several_invalid_settings_are_all_named_each_with_its_reason_and_its_line()
    {
        var parse = Parse("""
            image: node:22
            validation:
              - npm test
            methodology: nope
            correctionRounds: 99
            language: fr
            surprise: 1
            limits:
              memory: lots
            """);

        Assert.False(parse.IsValid);
        Assert.Equal(
            [
                ("methodology", MessageIds.ConfigIssueMethodologyUnavailable, (int?)4),
                ("correctionRounds", MessageIds.ConfigIssueRoundsInvalid, 5),
                ("language", MessageIds.ConfigIssueLanguageInvalid, 6),
                ("surprise", MessageIds.ConfigIssueUnknownKey, 7),
                ("limits.memory", MessageIds.ConfigIssueLimitMemoryInvalid, 9),
            ],
            parse.Issues.Select(i => (i.Setting, i.Reason.Id, i.Line)));
    }

    [Fact]
    public void A_key_outside_the_list_is_an_error_that_names_it_and_the_allowed_ones()
    {
        var issue = Single(Minimal + "stacking: on\n");

        AssertIssue(issue, "stacking", MessageIds.ConfigIssueUnknownKey);
        Assert.Contains("primaryAlias", (string)issue.Reason.Arguments[0], StringComparison.Ordinal);
        Assert.Contains("allowedEgressHosts", (string)issue.Reason.Arguments[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_is_case_sensitive_and_a_snake_case_key_is_unknown()
    {
        var parse = Parse(Minimal + "Image: x\ncorrection_rounds: 2\n");

        Assert.Equal(["Image", "correction_rounds"], parse.Issues.Select(i => i.Setting));
    }

    [Fact]
    public void A_key_that_is_not_a_name_is_an_error()
    {
        var issue = Single(Minimal + "? [a, b]\n: 1\n");

        AssertIssue(issue, "?", MessageIds.ConfigIssueUnknownKey);
    }

    [Fact]
    public void An_unknown_key_inside_limits_is_named_with_its_path()
    {
        var issue = Single(Minimal + "limits:\n  disk: 1g\n");

        AssertIssue(issue, "limits.disk", MessageIds.ConfigIssueUnknownKey);
        Assert.Equal("taskDuration, cpus, memory", issue.Reason.Arguments[0]);
    }

    [Fact]
    public void A_key_with_control_characters_and_a_long_value_are_made_safe_before_they_are_echoed()
    {
        var issue = Single(Minimal + "\"\\e[31mred\\u202Eevil\": 1\n");

        Assert.Equal("?[31mred?evil", issue.Setting);
        Assert.DoesNotContain(issue.Setting, c => char.IsControl(c));

        var long_ = Single(Minimal + "methodology: " + new string('x', 200) + "\n");
        AssertIssue(long_, "methodology", MessageIds.ConfigIssueMethodologyUnavailable);
        Assert.Equal(ConfigurationText.EchoLength + 1, ((string)long_.Reason.Arguments[0]).Length);
        Assert.EndsWith("…", (string)long_.Reason.Arguments[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Primary_and_Fallback_must_name_Aliases_that_set_an_Agent_and_a_Model()
    {
        Assert.Equal("opus5.5", Valid(Minimal + "primaryAlias: opus5.5\n").PrimaryAlias);
        Assert.Equal("sol6.2", Valid(Minimal + "fallbackAlias: sol6.2\n").FallbackAlias);

        AssertIssue(Single(Minimal + "primaryAlias: ghost\n"), "primaryAlias", MessageIds.ConfigIssueAliasUnknown);
        AssertIssue(Single(Minimal + "fallbackAlias: ghost\n"), "fallbackAlias", MessageIds.ConfigIssueAliasUnknown);
        AssertIssue(Single(Minimal + "primaryAlias: medium\n"), "primaryAlias", MessageIds.ConfigIssueAliasNotAgent);
        AssertIssue(Single(Minimal + "fallbackAlias: medium\n"), "fallbackAlias", MessageIds.ConfigIssueAliasNotAgent);
    }

    [Fact]
    public void Primary_and_Fallback_that_resolve_to_the_same_Agent_are_an_error_even_with_different_Models()
    {
        var issue = Single(Minimal + "primaryAlias: opus5.5\nfallbackAlias: sonnet5.5\n");

        AssertIssue(issue, "fallbackAlias", MessageIds.ConfigIssueAgentsSame);
        Assert.Equal(["claude-code"], issue.Reason.Arguments);
        Assert.Equal(5, issue.Line);
        Assert.True(Parse(Minimal + "primaryAlias: opus5.5\nfallbackAlias: sol6.2\n").IsValid);
    }

    [Fact]
    public void The_default_Effort_must_name_an_Alias_that_sets_an_Effort()
    {
        Assert.Equal("medium", Valid(Minimal + "effort: medium\n").Effort);

        AssertIssue(Single(Minimal + "effort: ghost\n"), "effort", MessageIds.ConfigIssueAliasUnknown);
        AssertIssue(Single(Minimal + "effort: opus5.5\n"), "effort", MessageIds.ConfigIssueAliasNotEffort);
    }

    [Fact]
    public void A_Methodology_must_be_one_the_installation_ships_and_the_message_lists_them()
    {
        var context = Context(null, "bmad", "default");

        Assert.Equal("bmad", Valid(Minimal + "methodology: bmad\n", context).Methodology);
        var issue = Single(Minimal + "methodology: waterfall\n", context);

        AssertIssue(issue, "methodology", MessageIds.ConfigIssueMethodologyUnavailable);
        Assert.Equal(["waterfall", "bmad, default"], issue.Reason.Arguments);
        AssertIssue(Single(Minimal + "methodology: BMAD\n", context), "methodology", MessageIds.ConfigIssueMethodologyUnavailable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("validation:\n")]
    [InlineData("validation: []\n")]
    [InlineData("validation: ~\n")]
    public void A_file_with_no_Validation_command_is_an_error_that_says_why(string validation)
    {
        var issue = Single("image: node:22\n" + validation);

        AssertIssue(issue, "validation", MessageIds.ConfigIssueValidationNone);
    }

    [Fact]
    public void A_Project_with_nothing_to_build_states_a_command_that_says_so()
    {
        Assert.Equal(["echo nothing to build"], Valid("image: alpine:3.22\nvalidation:\n  - echo nothing to build\n").Validation);
    }

    [Theory]
    [InlineData("validation: npm test\n", MessageIds.ConfigIssueExpectedList)]
    [InlineData("validation:\n  - npm test\n  - \"\"\n", MessageIds.ConfigIssueEmpty)]
    [InlineData("validation:\n  - npm test\n  - [a]\n", MessageIds.ConfigIssueExpectedText)]
    [InlineData("validation:\n  - \"npm\\u0000test\"\n", MessageIds.ConfigIssueCommandInvalid)]
    [InlineData("validation:\n  - \"npm\\e[1mtest\"\n", MessageIds.ConfigIssueCommandInvalid)]
    public void A_Validation_command_that_is_not_text_or_is_blank_or_holds_a_control_character_is_an_error_naming_it(string validation, string id)
    {
        var parse = Parse("image: node:22\n" + validation);

        var issue = Assert.Single(parse.Issues);
        Assert.Equal(id, issue.Reason.Id);
        Assert.StartsWith("validation", issue.Setting, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_Validation_command_is_named_by_its_position()
    {
        var issue = Single("image: node:22\nvalidation:\n  - npm test\n  - \"  \"\n");

        AssertIssue(issue, "validation[1]", MessageIds.ConfigIssueEmpty);
        Assert.Equal(4, issue.Line);
    }

    [Fact]
    public void A_multi_line_command_and_a_tab_are_allowed_and_the_number_of_commands_is_bounded()
    {
        Assert.Equal(["set -e\nnpm ci"], Valid("image: node:22\nvalidation:\n  - |\n    set -e\n    npm ci\n").Validation);
        Assert.Equal(["a\tb"], Valid("image: node:22\nvalidation:\n  - \"a\\tb\"\n").Validation);

        var many = string.Concat(Enumerable.Range(0, ProjectConfigurationFile.MaximumValidationCommands + 1).Select(i => $"  - cmd{i}\n"));
        AssertIssue(Single("image: node:22\nvalidation:\n" + many), "validation", MessageIds.ConfigIssueTooMany);
    }

    [Fact]
    public void A_command_that_is_too_long_is_an_error()
    {
        var command = new string('x', ProjectConfigurationFile.MaximumCommandLength + 1);

        AssertIssue(Single($"image: node:22\nvalidation:\n  - {command}\n"), "validation[0]", MessageIds.ConfigIssueTooLong);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("2", 2)]
    [InlineData("10", 10)]
    public void The_Correction_Loop_bound_is_a_whole_number_up_to_ten(string value, int expected) =>
        Assert.Equal(expected, Valid(Minimal + $"correctionRounds: {value}\n").CorrectionRounds);

    [Theory]
    [InlineData("11")]
    [InlineData("-1")]
    [InlineData("two")]
    [InlineData("1.5")]
    [InlineData("1000")]
    [InlineData("0x2")]
    public void A_Correction_Loop_bound_that_is_not_a_whole_number_up_to_ten_is_an_error(string value) =>
        AssertIssue(Single(Minimal + $"correctionRounds: {value}\n"), "correctionRounds", MessageIds.ConfigIssueRoundsInvalid);

    [Theory]
    [InlineData("correctionRounds: [1]", "correctionRounds")]
    [InlineData("limits:\n  memory: [4g]", "limits.memory")]
    [InlineData("allowedEgressHosts:\n  - [a.example.com]", "allowedEgressHosts[0]")]
    [InlineData("language: [en]", "language")]
    [InlineData("language:", "language")]
    [InlineData("methodology: {a: b}", "methodology")]
    [InlineData("primaryAlias:", "primaryAlias")]
    public void A_setting_that_is_not_a_single_value_is_an_error_naming_it(string setting, string expected)
    {
        var issue = Assert.Single(Parse(Minimal + setting + "\n").Issues);

        Assert.Equal(expected, issue.Setting);
        Assert.Contains(issue.Reason.Id, new[] { MessageIds.ConfigIssueExpectedText, MessageIds.ConfigIssueEmpty });
    }

    [Fact]
    public void A_value_that_is_too_long_is_an_error()
    {
        AssertIssue(Single(Minimal + "methodology: " + new string('m', 300) + "\n"), "methodology", MessageIds.ConfigIssueTooLong);
    }

    [Fact]
    public void The_image_is_required_and_a_missing_one_is_named()
    {
        AssertIssue(Single("validation:\n  - npm test\n"), "image", MessageIds.ConfigIssueRequired);
        AssertIssue(Single("image:\nvalidation:\n  - npm test\n"), "image", MessageIds.ConfigIssueEmpty);
        AssertIssue(Single("image: [a]\nvalidation:\n  - npm test\n"), "image", MessageIds.ConfigIssueExpectedText);
    }

    [Theory]
    [InlineData("node")]
    [InlineData("node:22")]
    [InlineData("node:22-alpine")]
    [InlineData("python:3.13-slim")]
    [InlineData("mcr.microsoft.com/dotnet/sdk:10.0")]
    [InlineData("ghcr.io/acme/builder:v1.2.3")]
    [InlineData("registry.local:5000/team/app")]
    [InlineData("library/ubuntu:24.04")]
    [InlineData("eclipse-temurin:21-jdk")]
    [InlineData("rust@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("golang:1.24@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void An_image_reference_is_accepted(string image) => Assert.Equal(image, Valid($"image: {image}\nvalidation:\n  - true\n").Image);

    [Theory]
    [InlineData("Node:22")]
    [InlineData("node:")]
    [InlineData("-node")]
    [InlineData("node 22")]
    [InlineData("node:22:23")]
    [InlineData("https://node:22")]
    [InlineData("node@sha256:abc")]
    [InlineData("a//b")]
    [InlineData("/node")]
    [InlineData("node/")]
    [InlineData("node:-1")]
    public void An_image_reference_that_is_not_one_is_an_error(string image)
    {
        var issue = Single($"image: \"{image}\"\nvalidation:\n  - true\n");

        AssertIssue(issue, "image", MessageIds.ConfigIssueImageInvalid);
    }

    [Fact]
    public void A_resource_limit_is_read_in_the_written_form_of_the_installation_limits()
    {
        var limits = Valid(Minimal + "limits:\n  taskDuration: 1h30m\n  cpus: \"0.5\"\n  memory: 512m\n").Limits;

        Assert.Equal(new ProjectLimits(TimeSpan.FromMinutes(90), 500, 512L * 1024 * 1024), limits);
        Assert.Equal(ProjectLimits.None, Valid(Minimal + "limits:\n").Limits);
    }

    [Theory]
    [InlineData("taskDuration", "soon", MessageIds.ConfigIssueLimitDurationInvalid)]
    [InlineData("taskDuration", "30s", MessageIds.ConfigIssueLimitDurationInvalid)]
    [InlineData("taskDuration", "8d", MessageIds.ConfigIssueLimitDurationInvalid)]
    [InlineData("cpus", "many", MessageIds.ConfigIssueLimitCpusInvalid)]
    [InlineData("cpus", "0.01", MessageIds.ConfigIssueLimitCpusInvalid)]
    [InlineData("memory", "lots", MessageIds.ConfigIssueLimitMemoryInvalid)]
    [InlineData("memory", "1m", MessageIds.ConfigIssueLimitMemoryInvalid)]
    [InlineData("memory", "99999999g", MessageIds.ConfigIssueLimitMemoryInvalid)]
    public void A_malformed_limit_is_an_error_naming_its_key(string key, string value, string id)
    {
        var issue = Single(Minimal + $"limits:\n  {key}: {value}\n");

        AssertIssue(issue, $"limits.{key}", id);
        Assert.Equal([value], issue.Reason.Arguments);
    }

    [Fact]
    public void A_limit_above_the_installations_is_an_error_and_a_limit_equal_to_it_is_not()
    {
        var installation = new InstallationLimits(TimeSpan.FromHours(2), 2000, 4L * 1024 * 1024 * 1024);

        Assert.True(Parse(Minimal + "limits:\n  taskDuration: 2h\n  cpus: 2\n  memory: 4g\n", Context(installation)).IsValid);

        var parse = Parse(Minimal + "limits:\n  taskDuration: 3h\n  cpus: 4\n  memory: 8g\n", Context(installation));
        Assert.Equal(["limits.taskDuration", "limits.cpus", "limits.memory"], parse.Issues.Select(i => i.Setting));
        Assert.All(parse.Issues, i => Assert.Equal(MessageIds.ConfigIssueLimitAboveInstallation, i.Reason.Id));
        Assert.Equal(["3h", "2h"], parse.Issues[0].Reason.Arguments);
        Assert.Equal(["4", "2"], parse.Issues[1].Reason.Arguments);
        Assert.Equal(["8g", "4g"], parse.Issues[2].Reason.Arguments);
    }

    [Fact]
    public void A_limit_the_installation_has_not_set_has_no_ceiling()
    {
        Assert.True(Parse(Minimal + "limits:\n  memory: 64g\n  cpus: 64\n  taskDuration: 7d\n", Context(new InstallationLimits(TimeSpan.FromHours(1), null, null))).Issues is [{ Setting: "limits.taskDuration" }]);
    }

    [Theory]
    [InlineData("limits: 5")]
    [InlineData("limits: [a]")]
    public void Limits_must_be_a_mapping(string limits) => AssertIssue(Single(Minimal + limits + "\n"), "limits", MessageIds.ConfigIssueExpectedMapping);

    [Theory]
    [InlineData("api.nuget.org")]
    [InlineData("registry.npmjs.org")]
    [InlineData("files.pythonhosted.org")]
    [InlineData("repo1.maven.org")]
    [InlineData("localhost")]
    [InlineData("a-b.example.com")]
    public void An_allowed_egress_host_is_a_host_name(string host) => Assert.Equal([host], Valid(Minimal + $"allowedEgressHosts:\n  - {host}\n").AllowedEgressHosts);

    [Theory]
    [InlineData("https://registry.npmjs.org")]
    [InlineData("registry.npmjs.org:443")]
    [InlineData("registry.npmjs.org/path")]
    [InlineData("*.npmjs.org")]
    [InlineData("10.0.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("-bad.example.com")]
    [InlineData("bad-.example.com")]
    [InlineData("a..b")]
    [InlineData("a b")]
    public void An_allowed_egress_host_that_is_not_a_host_name_is_an_error_naming_its_position(string host)
    {
        var issue = Single(Minimal + $"allowedEgressHosts:\n  - \"{host}\"\n");

        AssertIssue(issue, "allowedEgressHosts[0]", MessageIds.ConfigIssueHostInvalid);
        Assert.Equal([host], issue.Reason.Arguments);
    }

    [Fact]
    public void Egress_hosts_must_be_a_list_of_bounded_length_and_an_empty_one_means_none()
    {
        AssertIssue(Single(Minimal + "allowedEgressHosts: registry.npmjs.org\n"), "allowedEgressHosts", MessageIds.ConfigIssueExpectedList);
        Assert.Empty(Valid(Minimal + "allowedEgressHosts: []\n").AllowedEgressHosts);
        Assert.Empty(Valid(Minimal + "allowedEgressHosts:\n").AllowedEgressHosts);

        var many = string.Concat(Enumerable.Range(0, ProjectConfigurationFile.MaximumEgressHosts + 1).Select(i => $"  - h{i}.example.com\n"));
        AssertIssue(Single(Minimal + "allowedEgressHosts:\n" + many), "allowedEgressHosts", MessageIds.ConfigIssueTooMany);
    }

    [Theory]
    [InlineData("en", Language.English)]
    [InlineData("pl", Language.Polish)]
    public void The_language_is_en_or_pl(string tag, Language expected) => Assert.Equal(expected, Valid(Minimal + $"language: {tag}\n").Language);

    [Theory]
    [InlineData("fr")]
    [InlineData("EN")]
    [InlineData("english")]
    public void Any_other_language_is_an_error(string tag) => AssertIssue(Single(Minimal + $"language: {tag}\n"), "language", MessageIds.ConfigIssueLanguageInvalid);

    [Fact]
    public void A_file_that_is_not_UTF8_is_one_file_level_error()
    {
        var issue = Assert.Single(ProjectConfigurationParser.Parse([0xFF, 0xFE, 0x41, 0xC3, 0x28], Context()).Issues);

        AssertIssue(issue, "hephaestus.yml", MessageIds.ConfigIssueNotUtf8);
    }

    [Fact]
    public void A_file_larger_than_the_bound_is_one_file_level_error_and_is_not_parsed()
    {
        var content = Encoding.UTF8.GetBytes("# " + new string('x', ProjectConfigurationFile.MaximumLength));

        var issue = Assert.Single(ProjectConfigurationParser.Parse(content, Context()).Issues);

        AssertIssue(issue, "hephaestus.yml", MessageIds.ConfigIssueTooLarge);
        Assert.Equal([ProjectConfigurationFile.MaximumLength.ToString(System.Globalization.CultureInfo.InvariantCulture)], issue.Reason.Arguments);
    }

    [Fact]
    public void A_syntax_error_is_one_file_level_error_with_its_line_and_column_and_none_of_the_librarys_own_text()
    {
        var issue = Single("image: node:22\nvalidation: [npm test\nlanguage: en\n");

        Assert.Equal("hephaestus.yml", issue.Setting);
        Assert.Contains(issue.Reason.Id, new[] { MessageIds.ConfigIssueSyntax, MessageIds.ConfigIssueNotYaml });
    }

    [Fact]
    public void A_syntax_error_names_its_line_and_column()
    {
        var issue = Single("image: node:22\nvalidation:\n  - npm test\n\tbad: tab\n");

        AssertIssue(issue, "hephaestus.yml", MessageIds.ConfigIssueSyntax);
        Assert.InRange(issue.Line!.Value, 3, 4);
        Assert.Equal(issue.Line.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), issue.Reason.Arguments[0]);
        Assert.Equal(2, issue.Reason.Arguments.Length);
    }

    [Fact]
    public void A_key_written_twice_is_an_error_and_not_a_silent_last_one_wins()
    {
        var issue = Single(Minimal + "image: node:20\n");

        AssertIssue(issue, "hephaestus.yml", MessageIds.ConfigIssueDuplicateKey);
        Assert.Equal(4, issue.Line);
    }

    [Theory]
    [InlineData("image: node:22\nvalidation: [x]\nlimits:\n  memory: 1g\n  memory: 2g\n", 5)]
    [InlineData("image: node:22\nvalidation: [x]\nlimits: {cpus: 1, cpus: 2}\n", 3)]
    [InlineData("image: node:22\nvalidation: [x]\nlanguage: en\nlanguage: pl\n", 4)]
    [InlineData("image: node:22\nvalidation: [x]\nvalidation: [y]\n", 3)]
    public void A_key_written_twice_in_any_mapping_is_an_error_at_its_second_line(string yaml, int line)
    {
        var issue = Single(yaml);

        AssertIssue(issue, "hephaestus.yml", MessageIds.ConfigIssueDuplicateKey);
        Assert.Equal(line, issue.Line);
    }

    [Fact]
    public void The_same_key_in_different_mappings_and_a_list_of_equal_items_is_not_a_duplicate()
    {
        Assert.True(Parse("image: node:22\nvalidation: [x, x]\nlimits:\n  memory: 1g\nallowedEgressHosts: [a.example.com, a.example.com]\n").IsValid);
    }

    [Fact]
    public void Several_documents_are_an_error()
    {
        AssertIssue(Single(Minimal + "---\nimage: other\n"), "hephaestus.yml", MessageIds.ConfigIssueSeveralDocuments);
    }

    [Theory]
    [InlineData("- a\n- b\n")]
    [InlineData("just text\n")]
    [InlineData("42\n")]
    public void A_file_that_is_not_a_mapping_is_an_error(string content) => AssertIssue(Single(content), "hephaestus.yml", MessageIds.ConfigIssueNotMapping);

    [Theory]
    [InlineData("")]
    [InlineData("# only a comment\n")]
    [InlineData("---\n")]
    [InlineData("{}\n")]
    public void A_file_with_no_settings_names_what_it_lacks(string content)
    {
        var parse = Parse(content);

        Assert.Equal(["validation", "image"], parse.Issues.Select(i => i.Setting));
    }

    [Fact]
    public void Deep_nesting_is_refused_before_a_tree_is_built_from_it()
    {
        var nested = new string('[', 5000) + new string(']', 5000);

        var issue = Single("limits: " + nested + "\n");

        AssertIssue(issue, "hephaestus.yml", MessageIds.ConfigIssueTooDeep);
    }

    [Theory]
    [InlineData("image: &img node:22\nvalidation:\n  - npm test\nlimits: *img\n")]
    [InlineData("base: &base {memory: 1g}\nimage: node:22\nvalidation: [x]\nlimits:\n  <<: *base\n")]
    [InlineData("image: node:22\nvalidation: [x]\n? &a [*a]\n: 1\n")]
    [InlineData("image: node:22\nvalidation: [x]\n&a b: *a\n")]
    public void A_YAML_alias_is_an_error_whatever_it_points_at_so_that_nothing_a_file_builds_can_refer_to_itself(string yaml)
    {
        var issue = Single(yaml);

        AssertIssue(issue, "hephaestus.yml", MessageIds.ConfigIssueAliasNotAllowed);
        Assert.NotNull(issue.Line);
    }

    [Fact]
    public void An_anchor_that_nothing_refers_to_is_harmless_and_read_as_the_value_it_marks()
    {
        Assert.Equal(["npm test"], Valid("image: &img node:22\nvalidation:\n  - &cmd npm test\n").Validation);
    }

    [Fact]
    public void A_value_that_looks_like_another_type_is_read_as_the_text_it_is()
    {
        var configuration = Valid("image: node:22\nvalidation:\n  - true\n  - 1\n  - null\n");

        Assert.Equal(["true", "1", "null"], configuration.Validation);
    }

    private static string Arabic(int value) => string.Concat(value.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(digit => (char)('\u0660' + (digit - '0'))));

    [Theory]
    [InlineData("correctionRounds: {0}", "correctionRounds", MessageIds.ConfigIssueRoundsInvalid)]
    [InlineData("limits:\n  memory: {0}g", "limits.memory", MessageIds.ConfigIssueLimitMemoryInvalid)]
    [InlineData("limits:\n  cpus: {0}", "limits.cpus", MessageIds.ConfigIssueLimitCpusInvalid)]
    [InlineData("limits:\n  taskDuration: {0}m", "limits.taskDuration", MessageIds.ConfigIssueLimitDurationInvalid)]
    public void Digits_of_other_scripts_are_not_numbers_and_are_reported_not_crashed_on(string template, string setting, string id)
    {
        var issue = Single(Minimal + string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Arabic(3)) + "\n");

        AssertIssue(issue, setting, id);
    }

    [Fact]
    public void Digits_of_other_scripts_in_an_image_port_are_not_an_image_reference()
    {
        AssertIssue(Single($"image: registry.local:{Arabic(5000)}/team/app\nvalidation: [x]\n"), "image", MessageIds.ConfigIssueImageInvalid);
    }

    [Fact]
    public void An_omitted_Methodology_is_held_to_the_rule_a_named_one_is_so_an_installation_without_the_default_is_told()
    {
        AssertIssue(Single(Minimal, Context(null, "bmad")), "methodology", MessageIds.ConfigIssueMethodologyUnavailable);

        var none = Single(Minimal, new ConfigurationContext(Aliases, InstallationLimits.None, []));
        AssertIssue(none, "methodology", MessageIds.ConfigIssueMethodologyNoneShipped);
        Assert.Equal(["default", string.Empty], none.Reason.Arguments);
    }

    public static TheoryData<string, string> HostileValues => new()
    {
        { "language: \"{0}\"", "language" },
        { "image: \"a{0}\"", "image" },
        { "allowedEgressHosts:\n  - \"a{0}.example.com\"", "allowedEgressHosts[0]" },
        { "limits:\n  cpus: \"{0}\"", "limits.cpus" },
        { "limits:\n  memory: \"{0}\"", "limits.memory" },
        { "limits:\n  taskDuration: \"{0}\"", "limits.taskDuration" },
        { "limits:\n  \"{0}\": 1", "limits." },
        { "primaryAlias: \"{0}\"", "primaryAlias" },
        { "fallbackAlias: \"{0}\"", "fallbackAlias" },
        { "effort: \"{0}\"", "effort" },
        { "methodology: \"{0}\"", "methodology" },
        { "\"{0}\": 1", "" },
    };

    [Theory]
    [MemberData(nameof(HostileValues))]
    public void Text_from_the_file_that_a_message_carries_is_free_of_control_and_direction_characters(string template, string settingPrefix)
    {
        const string hostile = "\\e[2J\\u202E\\u2028x";
        var baseline = template.StartsWith("image:", StringComparison.Ordinal) ? "validation:\n  - npm test\n" : Minimal;
        var parse = Parse(baseline + template.Replace("{0}", hostile, StringComparison.Ordinal) + "\n");

        Assert.False(parse.IsValid);
        var issue = parse.Issues.First(i => i.Setting.StartsWith(settingPrefix, StringComparison.Ordinal) && (i.Reason.Arguments.Length > 0 || i.Setting.Contains('?', StringComparison.Ordinal)));
        var texts = issue.Reason.Arguments.Cast<string>().Append(issue.Setting).ToList();
        Assert.All(texts, text => Assert.DoesNotContain(text, c => char.IsControl(c) || c is '\u202E' or '\u2028'));
        Assert.Contains(texts, text => text.Contains('?', StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_with_carriage_returns_is_read_and_its_lines_are_counted_as_a_person_counts_them()
    {
        var parse = Parse("image: node:22\r\nvalidation:\r\n  - npm test\r\nlanguage: fr\r\n");

        var issue = Assert.Single(parse.Issues);
        AssertIssue(issue, "language", MessageIds.ConfigIssueLanguageInvalid);
        Assert.Equal(4, issue.Line);
    }
}
