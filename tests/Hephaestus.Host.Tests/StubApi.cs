namespace Hephaestus.Host.Tests;

/// <summary>A server on a temporary Unix socket that answers as a test needs, for answers the real host never gives.</summary>
internal sealed class StubApi : IAsyncDisposable
{
    private readonly TempSocket _socket;
    private readonly WebApplication _app;

    private StubApi(WebApplication app, TempSocket socket)
    {
        _app = app;
        _socket = socket;
    }

    public string SocketPath => _socket.Path;

    public static async Task<StubApi> StartAsync(Action<WebApplication> map)
    {
        var socket = new TempSocket();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenUnixSocket(socket.Path));
        var app = builder.Build();
        map(app);
        await app.StartAsync();
        return new StubApi(app, socket);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _socket.Dispose();
    }
}
