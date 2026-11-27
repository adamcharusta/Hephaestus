using System.Net;
using System.Text;

namespace Hephaestus.GitHub.Tests;

/// <summary>GitHub's REST API as far as the gateway calls it, in memory: the App, its installations, a token, and the repositories.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public const string InstallationToken = "ghs_installation-token-0123456789";

    public const string Permissions = """
        {"contents":"write","issues":"write","pull_requests":"write","metadata":"read","checks":"read","statuses":"read"}
        """;

    public List<(HttpMethod Method, string Path, string? Authorization)> Requests { get; } = [];

    public string AppJson { get; set; } = $$"""
        {"id":123456,"slug":"hephaestus-test","name":"Hephaestus Test","owner":{"login":"adamcharusta","id":1,"type":"User"},"permissions":{{Permissions}}}
        """;

    public string InstallationsJson { get; set; } = $$"""
        [
          {"id":11,"account":{"login":"adamcharusta","id":1,"type":"User"},"target_type":"User","repository_selection":"selected","permissions":{{Permissions}}},
          {"id":10,"account":{"login":"acme","id":2,"type":"Organization"},"target_type":"Organization","repository_selection":"all","permissions":{{Permissions}}}
        ]
        """;

    /// <summary>Repositories by installation id.</summary>
    public Dictionary<long, string[]> Repositories { get; } = new()
    {
        [10] = ["acme/web", "acme/api"],
        [11] = ["adamcharusta/Hephaestus"],
    };

    /// <summary>Users by login, as GitHub answers <c>GET /users/{login}</c>; GitHub's logins are not case sensitive.</summary>
    public Dictionary<string, string> Users { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alice"] = """{"login":"Alice","id":583231,"type":"User"}""",
        ["acme"] = """{"login":"acme","id":2,"type":"Organization"}""",
        ["hephaestus-test[bot]"] = """{"login":"hephaestus-test[bot]","id":99999,"type":"Bot"}""",
    };

    /// <summary>Branches by repository id and name, as GitHub answers <c>GET /repositories/{id}/branches/{name}</c>: whether each is protected.</summary>
    public Dictionary<string, bool> Branches { get; } = new()
    {
        ["1/main"] = true,
        ["1/release/1.0"] = true,
        ["1/a#b"] = false,
        ["1/a+b"] = true,
        ["2/main"] = false,
    };

    /// <summary>The commit at the tip of a branch by repository id and name; a branch that is not here has the commit <c>abc</c>.</summary>
    public Dictionary<string, string> Commits { get; } = [];

    /// <summary>Files by repository id, path, and the commit they are asked for at: <c>{id}/{path}@{commit}</c>, as GitHub answers <c>GET /repositories/{id}/contents/{path}?ref={commit}</c>.</summary>
    public Dictionary<string, byte[]> Files { get; } = [];

    /// <summary>The refs the contents were asked for at, in order: what the gateway reads a file at.</summary>
    public List<string> ContentRefs { get; } = [];

    /// <summary>Issue events by repository id: GitHub's id and the JSON of each, in any order.</summary>
    public Dictionary<long, List<(long Id, string Json)>> IssueEvents { get; } = [];

    /// <summary>Issue comments by repository id: when each was last updated and the JSON of each, in any order.</summary>
    public Dictionary<long, List<(DateTimeOffset UpdatedAt, string Json)>> Comments { get; } = [];

    /// <summary>The headers added to every answer of the event and comment lists, for example <c>X-Poll-Interval</c>.</summary>
    public Dictionary<string, string> ListHeaders { get; } = [];

    /// <summary>The polled lists asked for, in order: the path, the query, and the entity tag the request carried.</summary>
    public List<(string Path, string Query, string? IfNoneMatch)> ListRequests { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }

    public HttpStatusCode? AppStatus { get; set; }

    /// <summary>When set, no request is ever answered: GitHub accepted the connection and said nothing.</summary>
    public bool Hang { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add((request.Method, path, request.Headers.Authorization?.ToString()));

        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (Override?.Invoke(request) is { } overridden)
        {
            return overridden;
        }

        if (path == "/app" && AppStatus is { } status)
        {
            return Json(status, """{"message":"A JSON web token could not be decoded"}""");
        }

        return path switch
        {
            "/app" => Json(HttpStatusCode.OK, AppJson),
            "/app/installations" => Json(HttpStatusCode.OK, InstallationsJson),
            "/installation/repositories" => Json(HttpStatusCode.OK, RepositoriesOf(request)),
            _ when path.StartsWith("/repositories/", StringComparison.Ordinal) && path.EndsWith("/issues/events", StringComparison.Ordinal) => ListOf(request, path, "/issues/events"),
            _ when path.StartsWith("/repositories/", StringComparison.Ordinal) && path.EndsWith("/issues/comments", StringComparison.Ordinal) => ListOf(request, path, "/issues/comments"),
            _ when path.StartsWith("/repositories/", StringComparison.Ordinal) && path.Contains("/contents/", StringComparison.Ordinal) => ContentAt(request),
            _ when path.StartsWith("/repositories/", StringComparison.Ordinal) && path.Contains("/branches/", StringComparison.Ordinal) => BranchAt(path),
            _ when path.StartsWith("/users/", StringComparison.Ordinal) => Users.TryGetValue(Uri.UnescapeDataString(path["/users/".Length..]), out var user)
                ? Json(HttpStatusCode.OK, user)
                : Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
            _ when path.StartsWith("/app/installations/", StringComparison.Ordinal) && path.EndsWith("/access_tokens", StringComparison.Ordinal)
                => Json(HttpStatusCode.Created, $$"""{"token":"{{InstallationToken}}","expires_at":"2099-01-01T00:00:00Z"}"""),
            _ => Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
        };
    }

    /// <summary>
    /// GitHub's lists of a repository's issue events (newest first) and issue comments (by update time, from <c>since</c>), paged by
    /// <c>per_page</c> and <c>page</c> with a <c>Link</c> header, and answered with <c>304</c> to the entity tag of the first page.
    /// </summary>
    private HttpResponseMessage ListOf(HttpRequestMessage request, string path, string suffix)
    {
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var repository = long.Parse(path["/repositories/".Length..^suffix.Length], System.Globalization.CultureInfo.InvariantCulture);
        var ifNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString();
        ListRequests.Add((path, request.RequestUri.Query, ifNoneMatch));
        Assert.Equal(InstallationToken, request.Headers.Authorization!.Parameter);

        List<string> all;
        if (suffix == "/issues/events")
        {
            all = [.. IssueEvents.GetValueOrDefault(repository, []).OrderByDescending(e => e.Id).Select(e => e.Json)];
        }
        else
        {
            var since = query["since"] is { } text ? DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : DateTimeOffset.MinValue;
            all = [.. Comments.GetValueOrDefault(repository, []).Where(c => c.UpdatedAt >= since).OrderBy(c => c.UpdatedAt).Select(c => c.Json)];
        }

        var perPage = int.Parse(query["per_page"] ?? "30", System.Globalization.CultureInfo.InvariantCulture);
        var page = int.Parse(query["page"] ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        var entityTag = $"W/\"{System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(request.RequestUri.Query.Replace("&page=" + page, string.Empty, StringComparison.Ordinal) + string.Concat(all))).Aggregate(string.Empty, (hex, b) => hex + b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))[..16]}\"";

        HttpResponseMessage response;
        if (page == 1 && ifNoneMatch == entityTag)
        {
            response = new HttpResponseMessage(HttpStatusCode.NotModified);
        }
        else
        {
            response = Json(HttpStatusCode.OK, "[" + string.Join(',', all.Skip((page - 1) * perPage).Take(perPage)) + "]");
            if (page == 1)
            {
                response.Headers.TryAddWithoutValidation("ETag", entityTag);
            }

            if (page * perPage < all.Count)
            {
                var next = request.RequestUri.Query.Contains("&page=", StringComparison.Ordinal)
                    ? request.RequestUri.Query.Replace("&page=" + page, "&page=" + (page + 1), StringComparison.Ordinal)
                    : request.RequestUri.Query + "&page=2";
                response.Headers.TryAddWithoutValidation("Link", $"<https://api.github.com{path}{next}>; rel=\"next\"");
            }
        }

        foreach (var (name, value) in ListHeaders)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    private HttpResponseMessage BranchAt(string path)
    {
        var parts = Uri.UnescapeDataString(path["/repositories/".Length..]).Split("/branches/", 2);
        return Branches.TryGetValue(parts[0] + "/" + parts[1], out var isProtected)
            ? Json(HttpStatusCode.OK, $$$"""{"name":"{{{parts[1]}}}","protected":{{{(isProtected ? "true" : "false")}}},"commit":{"sha":"{{{Commits.GetValueOrDefault(parts[0] + "/" + parts[1], "abc")}}}","url":"https://api.github.com/x"}}""")
            : Json(HttpStatusCode.NotFound, """{"message":"Branch not found"}""");
    }

    private HttpResponseMessage ContentAt(HttpRequestMessage request)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath["/repositories/".Length..]);
        var id = path[..path.IndexOf('/', StringComparison.Ordinal)];
        var file = path[(path.IndexOf("/contents/", StringComparison.Ordinal) + "/contents/".Length)..];
        var reference = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["ref"] ?? string.Empty;
        ContentRefs.Add(reference);
        if (!Files.TryGetValue($"{id}/{file}@{reference}", out var bytes))
        {
            return Json(HttpStatusCode.NotFound, """{"message":"Not Found"}""");
        }

        var encoded = Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return Json(HttpStatusCode.OK, $$"""{"type":"file","encoding":"base64","size":{{bytes.Length}},"name":"{{file}}","path":"{{file}}","sha":"f00","content":"{{encoded}}"}""");
    }

    /// <summary>The repositories of the installation whose token the request carries; every token here belongs to the last one asked for.</summary>
    private string RepositoriesOf(HttpRequestMessage request)
    {
        Assert.Equal(InstallationToken, request.Headers.Authorization!.Parameter);
        var installation = Requests.Last(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal)).Path.Split('/')[3];
        var names = Repositories[long.Parse(installation, System.Globalization.CultureInfo.InvariantCulture)];
        var repositories = string.Join(',', names.Select((name, index) => $$"""{"id":{{index + 1}},"full_name":"{{name}}","name":"{{name.Split('/')[1]}}"}"""));
        return $$"""{"total_count":{{names.Length}},"repositories":[{{repositories}}]}""";
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
