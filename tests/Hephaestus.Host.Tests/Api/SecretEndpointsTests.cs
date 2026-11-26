using System.Net;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Host.Cli;
using Hephaestus.Host.Api;

namespace Hephaestus.Host.Tests.Api;

/// <summary>Story 1.5 on the control API: the endpoints that touch a secret, and what no response of theirs may contain.</summary>
public class SecretEndpointsTests
{
    private const string Value = "sk-test-0123456789-abcdefghij";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ControlApiClient ClientFor(RunningHost host, Language language = Language.English) => new(host.Settings.ControlSocketPath, language);

    [Fact]
    public async Task Put_stores_the_body_as_the_value_and_answers_the_name_alone()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/secrets/token", Value, Token);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal("""{"name":"token"}""", answer.Body);
        Assert.Equal(Value, await File.ReadAllTextAsync(Path.Combine(host.Settings.SecretsDirectory, "token"), Token));
    }

    [Fact]
    public async Task Get_lists_the_names_in_order_from_the_read_store()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/secrets/b", Value, Token);
        await client.SendAsync(HttpMethod.Put, "/api/secrets/a", Value, Token);

        var answer = await client.GetAsync("/api/secrets", Token);

        Assert.Equal("""{"secrets":[{"name":"a"},{"name":"b"}]}""", answer.Body);
    }

    [Fact]
    public async Task Delete_removes_the_secret_and_answers_the_name()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/secrets/token", Value, Token);

        var removed = await client.SendAsync(HttpMethod.Delete, "/api/secrets/token", body: null, Token);
        var again = await client.SendAsync(HttpMethod.Delete, "/api/secrets/token", body: null, Token);

        Assert.Equal("""{"name":"token"}""", removed.Body);
        Assert.Equal(HttpStatusCode.NotFound, again.Status);
        Assert.Equal("urn:hephaestus:problem:secret-not-found", again.Problem!.Type);
        Assert.False(File.Exists(Path.Combine(host.Settings.SecretsDirectory, "token")));
    }

    [Theory]
    [InlineData("", "secret-value-empty")]
    [InlineData("        ", "secret-value-empty")]
    [InlineData("1234567", "secret-value-too-short")]
    public async Task A_refused_value_is_a_400_problem_with_a_stable_type_and_the_value_is_not_in_it(string value, string slug)
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/secrets/token", value, Token);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal($"urn:hephaestus:problem:{slug}", answer.Problem!.Type);
        Assert.Equal(400, answer.Problem.Status);
        if (value.Length > 0 && !string.IsNullOrWhiteSpace(value))
        {
            Assert.DoesNotContain(value, answer.Body, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(Path.Combine(host.Settings.SecretsDirectory, "token")));
    }

    [Fact]
    public async Task A_value_over_the_maximum_is_a_400_problem_and_an_endless_body_is_read_no_further_than_that()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/secrets/token", new string('x', 1_000_000), Token);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("urn:hephaestus:problem:secret-value-too-long", answer.Problem!.Type);
    }

    [Fact]
    public async Task The_body_is_read_no_further_than_one_character_past_the_longest_value()
    {
        await using var endless = new EndlessStream();

        var value = await ControlApi.ReadValueAsync(endless, Token);

        Assert.Equal(Hephaestus.Core.Secrets.SecretPolicy.MaximumValueLength + 1, value.Length);
        Assert.True(endless.BytesServed < 1_000_000, $"{endless.BytesServed} bytes were read from a body that never ends.");
    }

    [Fact]
    public async Task A_value_with_the_replacement_character_is_a_400_problem_of_its_own_type()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/secrets/token", "secret-\uFFFD-value", Token);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("urn:hephaestus:problem:secret-value-not-text", answer.Problem!.Type);
        Assert.False(File.Exists(Path.Combine(host.Settings.SecretsDirectory, "token")));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_name_outside_the_rule_is_a_400_problem_for_a_write(string method)
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(new HttpMethod(method), "/api/secrets/Not%20Valid", Value, Token);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("urn:hephaestus:problem:secret-name-invalid", answer.Problem!.Type);
        Assert.DoesNotContain("Not Valid", answer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_problem_is_in_the_language_of_the_request_and_its_type_is_not()
    {
        await using var host = await RunningHost.StartAsync();
        using var english = ClientFor(host);
        using var polish = ClientFor(host, Language.Polish);

        var inEnglish = await english.SendAsync(HttpMethod.Put, "/api/secrets/token", "short", Token);
        var inPolish = await polish.SendAsync(HttpMethod.Put, "/api/secrets/token", "short", Token);

        Assert.Equal("Secret value too short", inEnglish.Problem!.Title);
        Assert.Equal("Wartość sekretu jest za krótka", inPolish.Problem!.Title);
        Assert.Equal(inEnglish.Problem.Type, inPolish.Problem.Type);
    }

    [Theory]
    [InlineData("POST", "/api/secrets/token")]
    [InlineData("GET", "/api/secrets/token")]
    [InlineData("PUT", "/api/secrets")]
    public async Task The_other_methods_on_the_secret_routes_are_refused(string method, string path)
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(new HttpMethod(method), path, Value, Token);

        Assert.Contains(answer.Status, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound });
        Assert.False(answer.IsSuccess);
        Assert.DoesNotContain(Value, answer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_response_of_any_operation_contains_the_value()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);
        var responses = new List<string>
        {
            (await client.SendAsync(HttpMethod.Put, "/api/secrets/token", Value, Token)).Body,
            (await client.SendAsync(HttpMethod.Put, "/api/secrets/token", Value, Token)).Body,
            (await client.GetAsync("/api/secrets", Token)).Body,
            (await client.SendAsync(HttpMethod.Put, "/api/secrets/Bad", Value, Token)).Body,
            (await client.SendAsync(HttpMethod.Delete, "/api/secrets/token", body: null, Token)).Body,
            (await client.SendAsync(HttpMethod.Delete, "/api/secrets/token", body: null, Token)).Body,
            (await client.GetAsync("/api/secrets", Token)).Body,
        };

        Assert.All(responses, body => Assert.DoesNotContain(Value, body, StringComparison.Ordinal));
        Assert.All(responses, body => JsonDocument.Parse(body).Dispose());
    }

    /// <summary>A body that never ends: the same byte again and again, counted.</summary>
    private sealed class EndlessStream : Stream
    {
        public long BytesServed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            buffer.Fill((byte)'x');
            BytesServed += buffer.Length;
            return buffer.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
