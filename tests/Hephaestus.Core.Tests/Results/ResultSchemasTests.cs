using System.Text.Json;
using Hephaestus.Core.Results;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Results;

/// <summary>Story 3.11, AD-13: the schema of a result is generated from the Core record and holds only what both CLIs' schema-constrained output accepts (Story 1.1).</summary>
public class ResultSchemasTests
{
    private static JsonElement Schema(StepKind kind) => JsonDocument.Parse(ResultSchemas.For(kind)!).RootElement;

    [Theory]
    [InlineData(StepKind.Implementation)]
    [InlineData(StepKind.Correction)]
    [InlineData(StepKind.Rework)]
    public void An_implementation_summary_is_the_result_of_a_step_that_changes_code(StepKind kind)
    {
        var schema = Schema(kind);

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal(["outcome", "summary", "questions"], schema.GetProperty("properties").EnumerateObject().Select(property => property.Name));
        Assert.Equal(ResultSchemas.For(StepKind.Implementation), ResultSchemas.For(kind));
    }

    [Theory]
    [InlineData(StepKind.Readiness)]
    [InlineData(StepKind.Planning)]
    [InlineData(StepKind.Review)]
    public void A_kind_whose_result_type_is_not_defined_yet_has_no_schema(StepKind kind) => Assert.Null(ResultSchemas.For(kind));

    [Fact]
    public void Every_property_is_required_and_no_other_is_allowed_at_every_level()
    {
        var schema = Schema(StepKind.Implementation);

        AssertClosed(schema);
        AssertClosed(schema.GetProperty("properties").GetProperty("questions").GetProperty("items"));
    }

    [Fact]
    public void The_outcome_is_one_of_the_two_kebab_case_values()
    {
        var outcome = Schema(StepKind.Implementation).GetProperty("properties").GetProperty("outcome");

        Assert.Equal("string", outcome.GetProperty("type").GetString());
        Assert.Equal(["completed", "needs-decision"], outcome.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void A_question_carries_its_options_as_strings()
    {
        var question = Schema(StepKind.Implementation).GetProperty("properties").GetProperty("questions").GetProperty("items");

        Assert.Equal(["question", "options"], question.GetProperty("properties").EnumerateObject().Select(property => property.Name));
        Assert.Equal("string", question.GetProperty("properties").GetProperty("options").GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public void The_generated_schema_uses_only_keywords_both_CLIs_accept()
    {
        Assert.Empty(ResultSchemas.Violations(ResultSchemas.For(StepKind.Implementation)!));
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"a":{"type":["string","null"]}}}""", "$.properties.a.type")]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string","minLength":1}}}""", "$.properties.a.minLength")]
    [InlineData("""{"$ref":"#/$defs/a"}""", "$.$ref")]
    [InlineData("""{"type":"array","items":{"oneOf":[]}}""", "$.items.oneOf")]
    [InlineData("""{"anyOf":[{"type":"string"}]}""", "$.anyOf")]
    public void A_schema_that_uses_something_else_is_found_out(string schema, string path)
    {
        Assert.Contains(path, ResultSchemas.Violations(schema));
    }

    [Fact]
    public void A_result_document_reads_back_by_strict_deserialization()
    {
        var result = JsonSerializer.Deserialize(
            """{"outcome":"needs-decision","summary":"Stopped.","questions":[{"question":"Which?","options":["a","b"]}]}""",
            ResultJson.Default.ImplementationResult);

        Assert.Equal(StepOutcome.NeedsDecision, result!.Outcome);
        Assert.Equal(["a", "b"], result.Questions.Single().Options);
    }

    [Fact]
    public void A_result_with_a_key_the_schema_does_not_have_is_not_read()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"outcome":"completed","summary":"Done.","questions":[],"extra":1}""",
            ResultJson.Default.ImplementationResult));
    }

    private static void AssertClosed(JsonElement schema)
    {
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            schema.GetProperty("properties").EnumerateObject().Select(property => property.Name),
            schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
    }
}
