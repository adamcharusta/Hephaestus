using Hephaestus.Core.Localization;
using Hephaestus.Host.Hosting;

namespace Hephaestus.Host.Tests;

/// <summary>The real host on a temporary socket, in this process.</summary>
internal sealed class RunningHost : IAsyncDisposable
{
    private readonly TempSocket _socket;

    private RunningHost(TempSocket socket, WebApplication app, HostSettings settings)
    {
        _socket = socket;
        App = app;
        Settings = settings;
    }

    public WebApplication App { get; }

    public HostSettings Settings { get; }

    public static async Task<RunningHost> StartAsync(
        Language language = Language.English,
        Action<WebApplication>? mapMore = null,
        Action<WebApplicationBuilder>? configure = null)
    {
        var socket = new TempSocket();
        var settings = new HostSettings(socket.Path, language);
        var app = ControlHost.Build(settings, configure);
        mapMore?.Invoke(app);
        await app.StartAsync();
        return new RunningHost(socket, app, settings);
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        _socket.Dispose();
    }
}
