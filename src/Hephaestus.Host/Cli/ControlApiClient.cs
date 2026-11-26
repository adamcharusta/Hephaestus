using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Host.Api;

namespace Hephaestus.Host.Cli;

/// <summary>What the API answered: the status, the body as it arrived, and the problem it describes when it failed.</summary>
internal sealed record ApiAnswer(HttpStatusCode Status, string Body, ProblemDocument? Problem)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;
}

/// <summary>Calls the control API over its Unix domain socket. The command line does nothing else (AD-17).</summary>
internal sealed class ControlApiClient : IDisposable
{
    private static readonly Uri BaseAddress = new("http://hephaestus.control/");

    private readonly HttpClient _http;

    public ControlApiClient(string socketPath, Language language)
    {
        _http = new HttpClient(new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        })
        {
            BaseAddress = BaseAddress,
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language.Tag()));
    }

    /// <summary>Throws <see cref="HttpRequestException"/> when nothing answers on the socket.</summary>
    public Task<ApiAnswer> GetAsync(string path, CancellationToken cancellationToken) => SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

    /// <summary>Sends a request, with a plain-text body when there is one. Throws like <see cref="GetAsync"/>.</summary>
    public async Task<ApiAnswer> SendAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), "text/plain");
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        var answer = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ApiAnswer(response.StatusCode, answer, response.IsSuccessStatusCode ? null : ReadProblem(response, answer));
    }

    public void Dispose() => _http.Dispose();

    private static ProblemDocument? ReadProblem(HttpResponseMessage response, string body)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, ProblemResponses.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var problem = JsonSerializer.Deserialize(body, ApiJson.Default.ProblemDocument);
            return problem is { Title: not null, Detail: not null } ? problem : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
