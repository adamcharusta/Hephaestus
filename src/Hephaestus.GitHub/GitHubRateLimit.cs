using System.Globalization;
using System.Net;
using Octokit;

namespace Hephaestus.GitHub;

/// <summary>
/// The one gate every request to GitHub passes (NFR-12): when GitHub answers with a rate-limit response, nothing is sent until the
/// time it states, whether the request came from the poller or from a command, and the wait counts as GitHub being unavailable, not as
/// a failure. The gate is shared by the gateway and the poller.
/// </summary>
public sealed class GitHubRateLimit(TimeProvider time)
{
    /// <summary>Longest wait honoured, so that a wrong header cannot stop Hephaestus from asking again for good (every wait is bounded).</summary>
    public static readonly TimeSpan MaximumWait = TimeSpan.FromHours(2);

    /// <summary>The wait for a rate-limit answer that states no time.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private DateTimeOffset _until = DateTimeOffset.MinValue;

    /// <summary>When the gate opens again; null when it is open now.</summary>
    public DateTimeOffset? ClosedUntil
    {
        get
        {
            lock (_gate)
            {
                return _until > time.GetUtcNow() ? _until : null;
            }
        }
    }

    /// <summary>Closes the gate until <paramref name="until"/>; an earlier time never shortens a wait that is already running.</summary>
    public void CloseUntil(DateTimeOffset until)
    {
        var latest = time.GetUtcNow() + MaximumWait;
        var bounded = until > latest ? latest : until;
        lock (_gate)
        {
            if (bounded > _until)
            {
                _until = bounded;
            }
        }
    }

    /// <summary>
    /// The time a response says to wait for when it is a rate-limit answer (a 429, or a 403 that carries <c>Retry-After</c>, a used-up
    /// primary limit, or says so in its message), and null when it is not one.
    /// </summary>
    public static DateTimeOffset? WaitOf(HttpStatusCode status, Func<string, string?> header, string? message, DateTimeOffset now)
    {
        if (status is not (HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden))
        {
            return null;
        }

        if (int.TryParse(header("Retry-After"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return now.AddSeconds(seconds);
        }

        var exhausted = header("X-RateLimit-Remaining") == "0";
        if (exhausted && long.TryParse(header("X-RateLimit-Reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var reset))
        {
            // The limit is lifted at the reset second; one more is a margin for the clocks. A time out of any reason is cut to the longest wait.
            return DateTimeOffset.FromUnixTimeSeconds(Math.Min(reset, now.ToUnixTimeSeconds() + (long)MaximumWait.TotalSeconds)).AddSeconds(1);
        }

        var says = message?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true;
        return status == HttpStatusCode.TooManyRequests || exhausted || says ? now + DefaultWait : null;
    }

    /// <summary>The same for an exception Octokit raised: the time to wait for, or null when it was not a rate-limit answer.</summary>
    public static DateTimeOffset? WaitOf(Exception exception, DateTimeOffset now)
    {
        if (exception is RateLimitExceededException primary && primary.Reset > now)
        {
            return primary.Reset.AddSeconds(1);
        }

        if (exception is not ApiException api)
        {
            return null;
        }

        var limited = api is RateLimitExceededException or SecondaryRateLimitExceededException or AbuseException;
        var headers = api.HttpResponse?.Headers;
        return WaitOf(
            limited ? HttpStatusCode.TooManyRequests : api.StatusCode,
            name => headers is not null && headers.TryGetValue(name, out var value) ? value : null,
            api.Message,
            now);
    }
}
