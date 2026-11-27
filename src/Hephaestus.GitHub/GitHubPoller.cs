using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Settings;
using Hephaestus.Core.Signals;
using Octokit;
using Project = Hephaestus.Core.Projects.Project;

namespace Hephaestus.GitHub;

/// <summary>What one polling cycle did, for the host's log: log lines are not translated.</summary>
/// <param name="ProjectsRead">How many registered Projects were asked about.</param>
/// <param name="SignalsAppended">How many Signals were new to the inbox.</param>
/// <param name="Problems">What went wrong, in English, one line each; a Project that could not be read does not stop the others.</param>
/// <param name="WaitUntil">When GitHub asked Hephaestus to wait until, if it did.</param>
/// <param name="Failure">The fault that ended the cycle when it was none of GitHub's doing, for the log; null otherwise.</param>
public sealed record PollCycleReport(int ProjectsRead, int SignalsAppended, IReadOnlyList<string> Problems, DateTimeOffset? WaitUntil, Exception? Failure = null)
{
    public static PollCycleReport Idle { get; } = new(0, 0, [], null);
}

/// <summary>
/// The poller (AD-4): the gateway's only background activity. Each cycle it asks GitHub, for every registered Project and with
/// conditional requests, what happened since its cursor, appends one Signal per event to the persisted inbox, and only then advances
/// the cursor (AD-1), so a crash in between re-reads events the inbox refuses as duplicates. It reads no repository that is not
/// registered and decides no trust: it appends Signals and keeps cursors, and changes no Task.
/// </summary>
public sealed class GitHubPoller : IDisposable
{
    private const string Api = "https://api.github.com";
    private const int PageSize = 100;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The longest the loop sleeps before it reads the polling interval again.</summary>
    private static readonly TimeSpan WaitSlice = TimeSpan.FromSeconds(15);

    private readonly IReadStore _reads;
    private readonly ISecretStore _secrets;
    private readonly ISignalInbox _inbox;
    private readonly IPollCursorStore _cursors;
    private readonly TimeProvider _time;
    private readonly GitHubRateLimit _limit;
    private readonly GitHubInstallations _installations;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly HttpClient _http;

    // GitHub's own minimum between two requests for the same repository (X-Poll-Interval); kept in memory because it only saves requests.
    private readonly Dictionary<long, DateTimeOffset> _notBefore = [];

    /// <param name="handler">The transport; a test gives its own. The default is Octokit's, which honours the proxy settings.</param>
    /// <param name="delay">How the loop waits between cycles; a test replaces it.</param>
    public GitHubPoller(
        IReadStore reads,
        ISecretStore secrets,
        ISignalInbox inbox,
        IPollCursorStore cursors,
        TimeProvider time,
        GitHubRateLimit limit,
        Func<HttpMessageHandler>? handler = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _reads = reads;
        _secrets = secrets;
        _inbox = inbox;
        _cursors = cursors;
        _time = time;
        _limit = limit;
        var transport = handler ?? (() => Octokit.Internal.HttpMessageHandlerFactory.CreateDefault());
        _installations = new GitHubInstallations(time, transport);
        _delay = delay ?? ((wait, token) => Task.Delay(wait, time, token));
        _http = new HttpClient(transport(), disposeHandler: handler is null) { Timeout = RequestTimeout };
    }

    /// <summary>
    /// Polls, waits the polling interval (or until GitHub says to try again, if that is later), and polls again, until stopped. The wait
    /// is taken in short slices that read the setting again, so a change of the interval is felt at once and not after a long wait.
    /// </summary>
    public async Task RunAsync(Action<PollCycleReport> onCycle, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var started = _time.GetUtcNow();
            PollCycleReport report;
            try
            {
                report = await PollOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A fault of the database or of a read must not end the poller: the cursors are intact, and the next cycle tries again.
                report = new PollCycleReport(0, 0, [$"The cycle failed: {exception.GetType().Name}: {exception.Message}"], null, exception);
            }

            onCycle(report);
            try
            {
                await WaitAsync(started, report.WaitUntil, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task WaitAsync(DateTimeOffset started, DateTimeOffset? waitUntil, CancellationToken cancellationToken)
    {
        while (true)
        {
            var due = started + await IntervalAsync(cancellationToken);
            if (waitUntil is { } until && until > due)
            {
                due = until;
            }

            var remaining = due - _time.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await _delay(remaining < WaitSlice ? remaining : WaitSlice, cancellationToken);
        }
    }

    /// <summary>One polling cycle: every registered Project, once.</summary>
    public async Task<PollCycleReport> PollOnceAsync(CancellationToken cancellationToken)
    {
        if (_limit.ClosedUntil is { } closed)
        {
            return new PollCycleReport(0, 0, [], closed);
        }

        var projects = await _reads.ListProjectsAsync(includeRemoved: false, cancellationToken);
        if (projects.Count == 0)
        {
            return PollCycleReport.Idle;
        }

        var appId = await _reads.GetGitHubAppIdAsync(cancellationToken);
        var privateKey = await _secrets.ReadAsync(GitHubAppCredential.PrivateKeySecretName, cancellationToken);
        if (appId is null || privateKey is null)
        {
            return new PollCycleReport(0, 0, ["GitHub is not connected, so nothing is polled."], null);
        }

        var problems = new List<string>();
        var tally = new Tally();
        try
        {
            await RefreshInstallationsAsync(appId.Value, privateKey, force: false, cancellationToken);
            foreach (var project in projects)
            {
                if (_notBefore.TryGetValue(project.RepositoryId, out var notBefore) && notBefore > _time.GetUtcNow())
                {
                    continue;
                }

                var token = _installations.TokenFor(project.RepositoryId);
                if (token is null && _installations.MaySearchAgain)
                {
                    await RefreshInstallationsAsync(appId.Value, privateKey, force: true, cancellationToken);
                    token = _installations.TokenFor(project.RepositoryId);
                }

                if (token is null)
                {
                    problems.Add($"{project.FullName}: the App is not installed on the repository.");
                    continue;
                }

                tally.Read++;
                await PollProjectAsync(project, token, problems, tally, cancellationToken);
            }
        }
        catch (PollInterruptedException interrupted)
        {
            problems.Add(interrupted.Message);
            return new PollCycleReport(tally.Read, tally.Appended, problems, interrupted.WaitUntil);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && GitHubGateway.IsOutage(exception))
        {
            var until = GitHubRateLimit.WaitOf(exception, _time.GetUtcNow());
            if (until is { } wait)
            {
                _limit.CloseUntil(wait);
            }

            problems.Add(until is null ? "GitHub did not answer; the next cycle tries again." : $"GitHub's rate limit is reached; waiting until {GitHubEvents.Instant(until.Value)}.");
            return new PollCycleReport(tally.Read, tally.Appended, problems, until);
        }
        catch (ApiException exception)
        {
            if (exception.StatusCode == HttpStatusCode.Unauthorized)
            {
                _installations.Forget();
            }

            problems.Add($"GitHub answered {(int)exception.StatusCode} when the App's installations were read; the next cycle tries again.");
            return new PollCycleReport(tally.Read, tally.Appended, problems, null);
        }

        return new PollCycleReport(tally.Read, tally.Appended, problems, null);
    }

    public void Dispose() => _http.Dispose();

    private async Task RefreshInstallationsAsync(long appId, string privateKey, bool force, CancellationToken cancellationToken)
    {
        if (!force && !_installations.IsStale)
        {
            return;
        }

        var jwt = GitHubAppJwt.TryCreate(appId, privateKey, _time.GetUtcNow())
            ?? throw new PollInterruptedException("The stored GitHub App key is not a PEM-encoded RSA private key.", null);
        if (_limit.ClosedUntil is { } closed)
        {
            throw new PollInterruptedException($"GitHub's rate limit is reached; waiting until {GitHubEvents.Instant(closed)}.", closed);
        }

        await _installations.LoadAsync(jwt, cancellationToken);
    }

    private async Task<TimeSpan> IntervalAsync(CancellationToken cancellationToken)
    {
        try
        {
            return InstallationSettings.PollingIntervalFrom(await _reads.ListSettingsAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return TimeSpan.FromSeconds(InstallationSettings.DefaultPollingIntervalSeconds);
        }
    }

    /// <summary>
    /// Each stream of the Project in turn. GitHub refusing one stream, or answering it with what is no list, is reported and does not
    /// stop the other stream or the other Projects; GitHub not answering, or asking to wait, ends the cycle.
    /// </summary>
    private async Task PollProjectAsync(Project project, string token, List<string> problems, Tally tally, CancellationToken cancellationToken)
    {
        var since = await _cursors.GetAsync(project.RepositoryId, PollStreams.Since, cancellationToken);
        var watchedSince = since is not null && GitHubEvents.TryParseInstant(since.Position, out var stored) ? stored : project.RegisteredAt;
        watchedSince = new DateTimeOffset(watchedSince.UtcDateTime.Ticks - (watchedSince.UtcDateTime.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

        try
        {
            tally.Appended += await PollIssueEventsAsync(project, token, watchedSince, cancellationToken);
        }
        catch (ProjectUnreadableException unreadable)
        {
            problems.Add($"{project.FullName}: {unreadable.Message}");
        }

        try
        {
            tally.Appended += await PollCommentsAsync(project, token, watchedSince, cancellationToken);
        }
        catch (ProjectUnreadableException unreadable)
        {
            problems.Add($"{project.FullName}: {unreadable.Message}");
        }
    }

    /// <summary>What a cycle has done so far, kept where an exception that ends the cycle cannot take it away.</summary>
    private sealed class Tally
    {
        public int Read { get; set; }

        public int Appended { get; set; }
    }

    /// <summary>
    /// The repository's issue events, newest first: pages are read until one reaches the cursor (or, with no cursor, the time the
    /// Project was registered), then the new events are appended oldest first and the cursor moves to the newest id seen.
    /// </summary>
    private async Task<int> PollIssueEventsAsync(Project project, string token, DateTimeOffset watchedSince, CancellationToken cancellationToken)
    {
        var cursor = await _cursors.GetAsync(project.RepositoryId, PollStreams.IssueEvents, cancellationToken);
        long? after = cursor is not null && long.TryParse(cursor.Position, NumberStyles.None, CultureInfo.InvariantCulture, out var stored) ? stored : null;

        var events = new List<IssueEventRead>();
        var newest = after ?? 0;
        string? entityTag = null;
        var url = $"{Api}/repositories/{project.RepositoryId.ToString(CultureInfo.InvariantCulture)}/issues/events?per_page={PageSize}";
        var first = true;
        var reachedOld = false;
        while (url is not null && !reachedOld)
        {
            var page = await GetAsync(project, url, token, first ? cursor?.ETag : null, cancellationToken);
            if (page.NotModified)
            {
                return 0;
            }

            entityTag = first ? page.ETag : entityTag;
            first = false;
            using (var document = Parse(page))
            {
                foreach (var element in document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray() : [])
                {
                    if (GitHubEvents.ReadIssueEvent(element, project.RepositoryId) is not { } read)
                    {
                        continue;
                    }

                    newest = Math.Max(newest, read.Id);
                    if (after is { } known ? read.Id <= known : read.CreatedAt < watchedSince)
                    {
                        reachedOld = true;
                        continue;
                    }

                    events.Add(read);
                }
            }

            url = page.Next;
        }

        var signals = events.Where(e => e.Signal is not null).OrderBy(e => e.Id).Select(e => e.Signal!).ToList();
        var appended = await _inbox.AppendAsync(signals, cancellationToken);
        await _cursors.SetAsync(project.RepositoryId, PollStreams.IssueEvents, new PollCursor(newest.ToString(CultureInfo.InvariantCulture), entityTag), cancellationToken);
        return appended;
    }

    /// <summary>
    /// The repository's comments updated since the cursor, oldest update first. The request starts a second before the cursor, because
    /// GitHub's times have a second's resolution; the inbox refuses what it already holds.
    /// </summary>
    private async Task<int> PollCommentsAsync(Project project, string token, DateTimeOffset watchedSince, CancellationToken cancellationToken)
    {
        var cursor = await _cursors.GetAsync(project.RepositoryId, PollStreams.Comments, cancellationToken);
        var position = cursor is not null && GitHubEvents.TryParseInstant(cursor.Position, out var stored) ? stored : watchedSince;

        var signals = new List<Signal>();
        var newest = position;
        string? entityTag = null;
        var url = $"{Api}/repositories/{project.RepositoryId.ToString(CultureInfo.InvariantCulture)}/issues/comments?sort=updated&direction=asc&per_page={PageSize}&since={GitHubEvents.Instant(position.AddSeconds(-1))}";
        var first = true;
        while (url is not null)
        {
            var page = await GetAsync(project, url, token, first ? cursor?.ETag : null, cancellationToken);
            if (page.NotModified)
            {
                return 0;
            }

            entityTag = first ? page.ETag : entityTag;
            first = false;
            using (var document = Parse(page))
            {
                foreach (var element in document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray() : [])
                {
                    if (GitHubEvents.ReadComment(element, project.RepositoryId, watchedSince) is { } read)
                    {
                        signals.AddRange(read.Signals);
                        newest = read.UpdatedAt > newest ? read.UpdatedAt : newest;
                    }
                }
            }

            url = page.Next;
        }

        var appended = await _inbox.AppendAsync([.. signals.OrderBy(s => s.OccurredAt).ThenBy(s => s.Kind).ThenBy(s => s.CommentId)], cancellationToken);
        await _cursors.SetAsync(project.RepositoryId, PollStreams.Comments, new PollCursor(GitHubEvents.Instant(newest), entityTag), cancellationToken);
        return appended;
    }

    private sealed record Page(bool NotModified, string? ETag, string Body, string? Next);

    /// <summary>An answer that is not JSON (a proxy's error page, say) is this Project's trouble and not the end of the cycle.</summary>
    private static JsonDocument Parse(Page page)
    {
        try
        {
            return JsonDocument.Parse(page.Body);
        }
        catch (JsonException)
        {
            throw new ProjectUnreadableException("GitHub's answer is not JSON.");
        }
    }

    /// <summary>
    /// One conditional request. A rate-limit answer closes the gate until the time GitHub states, and the cycle ends; GitHub not
    /// answering ends it too. A refusal for this repository alone is reported and the other Projects are still read.
    /// </summary>
    private async Task<Page> GetAsync(Project project, string url, string token, string? entityTag, CancellationToken cancellationToken)
    {
        // A command may have met the rate limit since the cycle began: nothing is sent while the gate is closed.
        if (_limit.ClosedUntil is { } closed)
        {
            throw new PollInterruptedException($"GitHub's rate limit is reached; waiting until {GitHubEvents.Instant(closed)}.", closed);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd($"Hephaestus/{Core.HephaestusVersion.Current}");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (entityTag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", entityTag);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is HttpRequestException or TaskCanceledException)
        {
            throw new PollInterruptedException("GitHub did not answer; the next cycle tries again.", null);
        }

        using (response)
        {
            var now = _time.GetUtcNow();
            if (response.Headers.TryGetValues("X-Poll-Interval", out var interval) && int.TryParse(interval.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                _notBefore[project.RepositoryId] = now + TimeSpan.FromSeconds(Math.Min(seconds, GitHubRateLimit.MaximumWait.TotalSeconds));
            }

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new Page(NotModified: true, entityTag, string.Empty, null);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new Page(NotModified: false, response.Headers.ETag?.ToString(), body, NextLink(response));
            }

            if (GitHubRateLimit.WaitOf(response.StatusCode, name => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null, body, now) is { } until)
            {
                _limit.CloseUntil(until);
                throw new PollInterruptedException($"GitHub's rate limit is reached; waiting until {GitHubEvents.Instant(until)}.", until);
            }

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                throw new PollInterruptedException($"GitHub answered {(int)response.StatusCode}; the next cycle tries again.", null);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _installations.Forget();
                throw new PollInterruptedException("GitHub no longer accepts the installation token; the next cycle asks for a new one.", null);
            }

            throw new ProjectUnreadableException($"GitHub answered {(int)response.StatusCode}.");
        }
    }

    /// <summary>The URL of the next page: the <c>next</c> relation of the <c>Link</c> header, when it names GitHub's API.</summary>
    private static string? NextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var link in values.SelectMany(value => value.Split(',')))
        {
            var parts = link.Split(';', 2);
            if (parts.Length == 2 && parts[1].Contains("rel=\"next\"", StringComparison.Ordinal))
            {
                var target = parts[0].Trim().Trim('<', '>');

                // A page of one request is never followed to another host, which would carry the token with it.
                return target.StartsWith(Api + "/", StringComparison.Ordinal) ? target : null;
            }
        }

        return null;
    }

    /// <summary>GitHub cannot be read now, or asked for a wait: the cycle ends and every cursor is as it was.</summary>
    private sealed class PollInterruptedException(string message, DateTimeOffset? waitUntil) : Exception(message)
    {
        public DateTimeOffset? WaitUntil { get; } = waitUntil;
    }

    /// <summary>GitHub refused this repository; the others are still read.</summary>
    private sealed class ProjectUnreadableException(string message) : Exception(message);
}
