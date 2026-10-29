using System.Net;
using DhurtaHost.Api;
using DhurtaHost.Handlers;
using DhurtaHost.Middleware;
using DhurtaHost.Services;
using DhurtaHost.Tray;

// Must be STA for Windows Forms tray icon
[STAThread]
static void Main(string[] args)
{
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    var cts = new CancellationTokenSource();

    // Build ASP.NET Core host
    var host = BuildWebHost(args);

    // Initialize services that require it
    host.Services.GetRequiredService<DatabaseService>().Initialize();
    var auth = host.Services.GetRequiredService<AuthService>();
    auth.Initialize();
    host.Services.GetRequiredService<DeviceService>().Initialize();

    // Configure and start screen capture
    var capture = host.Services.GetRequiredService<ScreenCaptureService>();
    var config = host.Services.GetRequiredService<IConfiguration>();
    capture.Configure(
        fps: config.GetValue("Capture:Fps", 15),
        jpegQuality: config.GetValue("Capture:JpegQuality", 70));
    capture.Start();

    // Start web host on background threads
    var hostTask = host.StartAsync(cts.Token);
    hostTask.GetAwaiter().GetResult(); // wait until Kestrel is listening

    // Start Cloudflare Tunnel
    host.Services.GetRequiredService<TunnelService>().Start();

    // Run tray application on the STA main thread
    Application.Run(new TrayApplicationContext(host, host.Services, cts));

    // Cleanup after tray exits
    cts.Cancel();
    host.Services.GetRequiredService<ScreenCaptureService>().Stop();
    host.Services.GetRequiredService<TunnelService>().Stop();
    host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
}

static IHost BuildWebHost(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Services ──────────────────────────────────────────────────────────
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

    // ── Kestrel: listen on localhost only ─────────────────────────────────
    // The tunnel handles public exposure; we never bind to 0.0.0.0
    builder.WebHost.ConfigureKestrel(opts =>
    {
        opts.Listen(IPAddress.Loopback, 8765);
    });

    var app = builder.Build();

    // ── Security headers ──────────────────────────────────────────────────
    app.Use(async (ctx, next) =>
    {
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        ctx.Response.Headers["Cache-Control"] = "no-store";
        await next();
    });

    // ── CORS ──────────────────────────────────────────────────────────────
    // Allow the GitHub Pages frontend and local dev server
    app.UseCors(policy =>
    {
        var frontendOrigin = app.Configuration["FrontendOrigin"]
                             ?? "https://prashantkeshr.github.io";
        policy.WithOrigins(frontendOrigin, "http://localhost:5173")
              .AllowAnyHeader()
              .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
              .AllowCredentials();
    });

    // ── Rate limiting ─────────────────────────────────────────────────────
    app.UseMiddleware<RateLimitMiddleware>();

    // ── WebSockets ────────────────────────────────────────────────────────
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

    // ── REST endpoints ────────────────────────────────────────────────────
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

// Entry point declaration for top-level statements
public partial class Program { }
