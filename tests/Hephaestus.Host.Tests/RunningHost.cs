using Hephaestus.Core.Localization;
using Hephaestus.Host.Hosting;

namespace Hephaestus.Host.Tests;

/// <summary>The real host on a temporary socket, in this process.</summary>
internal sealed class RunningHost : IAsyncDisposable
{
    private readonly TempSocket _socket;
    private readonly bool _ownsInstallation;

    private RunningHost(TempSocket socket, bool ownsInstallation, WebApplication app, HostSettings settings)
    {
        _socket = socket;
        _ownsInstallation = ownsInstallation;
        App = app;
        Settings = settings;
    }

    public WebApplication App { get; }

    public HostSettings Settings { get; }

    public TempSocket Installation => _socket;

    public static async Task<RunningHost> StartAsync(
        Language language = Language.English,
        Action<WebApplication>? mapMore = null,
        Action<WebApplicationBuilder>? configure = null,
        TempSocket? installation = null)
    {
        var socket = installation ?? new TempSocket();
        var settings = socket.Settings(language);
        var app = ControlHost.Build(settings, configure);
        mapMore?.Invoke(app);
        await ControlHost.MigrateAsync(app, TestContext.Current.CancellationToken);
        await app.StartAsync();
        return new RunningHost(socket, installation is null, app, settings);
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        if (_ownsInstallation)
        {
            _socket.Dispose();
        }
    }
}
