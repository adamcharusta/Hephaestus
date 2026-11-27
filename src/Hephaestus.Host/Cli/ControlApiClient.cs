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

    /// <summary>How long a call may take unless its command says otherwise.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long <c>agent check</c> waits: the host runs a probe that may take up to two minutes, and the first one fetches the probe image. A call that gave up sooner would leave the host spending
    /// tokens, and recording a finding, that the Owner never sees.
    /// </summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(15);

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
            // The limit of a call is its own (below), so that a command that waits for the host's work can wait longer than one that asks a question.
            Timeout = Timeout.InfiniteTimeSpan,
        };
        UseLanguage(language);
    }

    /// <summary>Names the language the command line writes in, so that the host answers its problems in the same one.</summary>
    public void UseLanguage(Language language)
    {
        _http.DefaultRequestHeaders.AcceptLanguage.Clear();
        _http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language.Tag()));
    }

    /// <summary>Throws <see cref="HttpRequestException"/> when nothing answers on the socket.</summary>
    public Task<ApiAnswer> GetAsync(string path, CancellationToken cancellationToken) => SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

    /// <summary>Sends a request, with a plain-text body when there is one. Throws like <see cref="GetAsync"/>.</summary>
    public async Task<ApiAnswer> SendAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout ?? DefaultTimeout);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), "text/plain");
        }

        try
        {
            using var response = await _http.SendAsync(request, limit.Token);
            var answer = await response.Content.ReadAsStringAsync(limit.Token);
            return new ApiAnswer(response.StatusCode, answer, response.IsSuccessStatusCode ? null : ReadProblem(response, answer));
        }
        catch (OperationCanceledException exception) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested && exception is not TaskCanceledException)
        {
            // The limit passed while the answer was being read: a call that is too slow is one the host did not answer, as it is when it is waiting for the headers.
            throw new TaskCanceledException("The host did not answer in time.", exception);
        }
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
