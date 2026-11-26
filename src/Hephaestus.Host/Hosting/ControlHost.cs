using System.Net.Sockets;
using System.Text.Json;
using Hephaestus.Host.Api;
using Hephaestus.Persistence;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// Builds the host: Kestrel on the control Unix socket and nothing else, JSON logs on stdout, problem details, and the storage
/// ports of Persistence.
/// </summary>
internal static class ControlHost
{
    public static WebApplication Build(HostSettings settings, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        configure?.Invoke(builder);

        // A Kestrel endpoint from configuration (Kestrel__Endpoints__...) would add a listener beside the socket, and unlike an
        // address named by ASPNETCORE_URLS it is not replaced by the endpoints given in code below.
        if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            throw new InvalidOperationException("The control API listens on its Unix socket only; remove the Kestrel endpoints from the configuration.");
        }

        builder.Services.AddSingleton(settings);
        builder.Services.AddHephaestusPersistence(settings.Persistence);
        builder.Logging.ClearProviders();
        // The container's health check calls the API every few seconds; its request lines are not worth a log line each.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
        });

        PrepareSocketPath(settings.ControlSocketPath);

        // Kestrel endpoints given in code replace any address the environment names (ASPNETCORE_URLS, ASPNETCORE_HTTP_PORTS),
        // so the control API cannot end up on a network address.
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.ListenUnixSocket(settings.ControlSocketPath, listen => listen.Protocols = HttpProtocols.Http1);
        });

        var app = builder.Build();

        app.UseExceptionHandler(errors => errors.Run(context => ProblemResponses.WriteUnexpectedAsync(context, settings)));
        app.UseStatusCodePages(context => ProblemResponses.WriteAsync(context.HttpContext, settings));
        ControlApi.Map(app);

        app.Lifetime.ApplicationStarted.Register(() => RestrictSocket(app, settings.ControlSocketPath));
        return app;
    }

    /// <summary>
    /// Creates the database when it is missing and applies its migrations. The host does this before it listens, and a failure
    /// stops it: an API on a database in an unknown state would be worse than none.
    /// </summary>
    public static async Task MigrateAsync(WebApplication app, CancellationToken cancellationToken)
    {
        await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(cancellationToken);
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Hephaestus.Host")
            .LogInformation("Database ready at {DatabasePath}", app.Services.GetRequiredService<PersistenceOptions>().DatabasePath);
    }

    /// <summary>
    /// Makes room for the socket: a directory only its owner can enter when this creates it, and no file left by an earlier run.
    /// A socket file outlives the process that made it, and a restarted container keeps it; Kestrel will not bind over it.
    /// A socket that still answers belongs to a running host, which a second one must not unlink.
    /// </summary>
    private static void PrepareSocketPath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directory);
            }
            else
            {
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        if (!File.Exists(path))
        {
            return;
        }

        if (IsAnswering(path))
        {
            throw new InvalidOperationException($"Another Hephaestus host already answers on {path}.");
        }

        File.Delete(path);
    }

    private static bool IsAnswering(string path)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(path));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Only the user the host runs as may open the control socket.</summary>
    private static void RestrictSocket(WebApplication app, string path)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Hephaestus.Host");
        if (!OperatingSystem.IsWindows() && File.Exists(path))
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        logger.LogInformation("Control API listening on {SocketPath}", path);
    }
}
