using System.Net;
using System.Security.Cryptography;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Projects;

namespace Hephaestus.GitHub.Tests;

/// <summary>A clock a test moves.</summary>
internal sealed class MovingTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>The poller over in-memory ports and a fake GitHub: one registered Project, <c>acme/web</c> with the repository id 1.</summary>
internal sealed class PollerFixture : IDisposable
{
    public const long RepositoryId = 1;
    public static readonly DateTimeOffset RegisteredAt = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    public PollerFixture()
    {
        Stores = new FakeStores { AppId = 123456, Secrets = { [GitHubAppCredential.PrivateKeySecretName] = RSA.Create(2048).ExportRSAPrivateKeyPem() } };
        Stores.Projects.Add(new Project(RepositoryId, "acme/web", "main", RegisteredAt, null));
        Time = new MovingTime(RegisteredAt.AddMinutes(5));
        Limit = new GitHubRateLimit(Time);
        Poller = new GitHubPoller(Stores, Stores, Inbox, Cursors, Time, Limit, () => GitHub, delay: (wait, _) =>
        {
            Waits.Add(wait);
            Time.Now += wait;
            OnWait?.Invoke();
            return Task.CompletedTask;
        });
    }

    public FakeStores Stores { get; }

    public FakeGitHub GitHub { get; } = new();

    public FakeInbox Inbox { get; } = new();

    public FakeCursors Cursors { get; } = new();

    public MovingTime Time { get; }

    public GitHubRateLimit Limit { get; }

    public GitHubPoller Poller { get; }

    /// <summary>How long the loop slept each time it slept, in order; the clock moves by that much.</summary>
    public List<TimeSpan> Waits { get; } = [];

    /// <summary>Run each time the loop has slept.</summary>
    public Action? OnWait { get; set; }

    /// <summary>The time the loop has slept altogether.</summary>
    public TimeSpan Slept => TimeSpan.FromTicks(Waits.Sum(w => w.Ticks));

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public Task<PollCycleReport> PollAsync() => Poller.PollOnceAsync(Token);

    public int ListRequestsFor(string suffix) => GitHub.ListRequests.Count(r => r.Path.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>An issue event as GitHub lists it.</summary>
    public static (long Id, string Json) Event(long id, string kind, int issue, long? actor, string at = "2026-11-27T12:10:00Z", string? label = null, bool pullRequest = false)
    {
        var who = actor is { } a ? $$"""{"login":"user{{a}}","id":{{a}}}""" : "null";
        var what = label is null ? string.Empty : $$""","label":{"name":"{{label}}"}""";
        var pull = pullRequest ? ""","pull_request":{"url":"https://api.github.com/x"}""" : string.Empty;
        var issueJson = "{\"number\":" + issue + pull + "}";
        return (id, $$"""{"id":{{id}},"actor":{{who}},"event":"{{kind}}","created_at":"{{at}}"{{what}},"issue":""" + issueJson + "}");
    }

    /// <summary>An issue comment as GitHub lists it.</summary>
    public static (DateTimeOffset UpdatedAt, string Json) Comment(long id, int issue, long author, string created, string updated) =>
        (DateTimeOffset.Parse(updated, System.Globalization.CultureInfo.InvariantCulture),
            $$"""{"id":{{id}},"user":{"login":"user{{author}}","id":{{author}}},"created_at":"{{created}}","updated_at":"{{updated}}","issue_url":"https://api.github.com/repos/acme/web/issues/{{issue}}"}""");

    public static HttpResponseMessage RateLimited(HttpStatusCode status, string? retryAfter = null, string? remaining = null, long? reset = null, string message = "API rate limit exceeded")
    {
        var response = FakeGitHub.Json(status, $$"""{"message":"{{message}}"}""");
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        if (remaining is not null)
        {
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", remaining);
        }

        if (reset is not null)
        {
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", reset.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return response;
    }

    public void Dispose() => Poller.Dispose();
}
