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
            _ when path.StartsWith("/app/installations/", StringComparison.Ordinal) && path.EndsWith("/access_tokens", StringComparison.Ordinal)
                => Json(HttpStatusCode.Created, $$"""{"token":"{{InstallationToken}}","expires_at":"2099-01-01T00:00:00Z"}"""),
            _ => Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
        };
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
