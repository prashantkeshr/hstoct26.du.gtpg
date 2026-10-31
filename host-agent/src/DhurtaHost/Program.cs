using System.Net;
using DhurtaHost.Api;
using DhurtaHost.Handlers;
using DhurtaHost.Middleware;
using DhurtaHost.Services;
using DhurtaHost.Tray;

public partial class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatalError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ShowFatalError(ex);
        };

        // Enforce single instance — second launch shows tray hint and exits
        using var mutex = new Mutex(true, "Global\\DhurtaHostSingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show(
                "Dhurta Host is already running." + Environment.NewLine +
                "Look for its icon in the system tray (notification area, bottom-right of your taskbar).",
                "Dhurta Host",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            var cts = new CancellationTokenSource();

            var host = BuildWebHost(args);

            host.Services.GetRequiredService<DatabaseService>().Initialize();
            var auth = host.Services.GetRequiredService<AuthService>();
            auth.Initialize();
            host.Services.GetRequiredService<DeviceService>().Initialize();

            var capture = host.Services.GetRequiredService<ScreenCaptureService>();
            var config = host.Services.GetRequiredService<IConfiguration>();
            capture.Configure(
                fps: config.GetValue("Capture:Fps", 15),
                jpegQuality: config.GetValue("Capture:JpegQuality", 70));
            capture.Start();

            host.StartAsync(cts.Token).GetAwaiter().GetResult();

            host.Services.GetRequiredService<TunnelService>().Start();

            Application.Run(new TrayApplicationContext(host, host.Services, cts));

            cts.Cancel();
            host.Services.GetRequiredService<ScreenCaptureService>().Stop();
            host.Services.GetRequiredService<TunnelService>().Stop();
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ShowFatalError(ex);
        }
    }

    static void ShowFatalError(Exception ex)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DhurtaHost");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "startup-error.log");
        try
        {
            File.AppendAllText(logPath,
                $"[{DateTime.Now:O}]{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }

        MessageBox.Show(
            $"Dhurta Host failed to start:{Environment.NewLine}{Environment.NewLine}{ex.Message}" +
            $"{Environment.NewLine}{Environment.NewLine}Full error log:{Environment.NewLine}{logPath}",
            "Dhurta Host — Startup Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    static IHost BuildWebHost(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<DeviceService>();
        builder.Services.AddSingleton<ScreenCaptureService>();
        builder.Services.AddSingleton<InputService>();
        builder.Services.AddSingleton<TunnelService>();
        builder.Services.AddSingleton<RateLimiterService>();
        builder.Services.AddSingleton<WebRtcService>();
        builder.Services.AddSingleton<WebSocketHandler>();

        builder.Services.AddCors();
        builder.Services.AddLogging(l =>
        {
            l.AddConsole();
            l.SetMinimumLevel(LogLevel.Information);
        });

        builder.WebHost.ConfigureKestrel(opts =>
        {
            opts.Listen(IPAddress.Loopback, 8765);
        });

        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["X-Frame-Options"] = "DENY";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await next();
        });

        app.UseCors(policy =>
        {
            var frontendOrigin = app.Configuration["FrontendOrigin"]
                                 ?? "https://prashantkeshr.github.io";
            policy.WithOrigins(frontendOrigin, "http://localhost:5173")
                  .AllowAnyHeader()
                  .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                  .AllowCredentials();
        });

        app.UseMiddleware<RateLimitMiddleware>();

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        app.Map("/ws", async ctx =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 426;
                await ctx.Response.WriteAsJsonAsync(new { error = "WebSocket upgrade required" });
                return;
            }
            var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await ctx.RequestServices
                .GetRequiredService<WebSocketHandler>()
                .HandleAsync(ws, ctx.RequestAborted);
        });

        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
            timestamp = DateTime.UtcNow,
        }));

        app.MapAuthEndpoints();
        app.MapDeviceEndpoints();

        return app;
    }
}
