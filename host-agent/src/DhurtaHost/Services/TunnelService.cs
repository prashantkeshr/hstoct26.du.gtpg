using System.Diagnostics;

namespace DhurtaHost.Services;

public sealed class TunnelService : IDisposable
{
    private readonly ILogger<TunnelService> _log;
    private readonly IConfiguration _cfg;
    private Process? _cloudflared;
    private bool _running;

    public string? PublicUrl { get; private set; }
    public event EventHandler<string>? UrlChanged;

    public TunnelService(ILogger<TunnelService> log, IConfiguration cfg)
    {
        _log = log;
        _cfg = cfg;
    }

    public void Start(int localPort = 8765)
    {
        if (_running) return;

        var exePath = FindCloudflared();
        if (exePath is null)
        {
            _log.LogWarning("cloudflared not found. Tunnel disabled. Install from https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/");
            return;
        }

        var configPath = GetConfigPath();
        var args = File.Exists(configPath)
            ? $"tunnel --config \"{configPath}\" run"
            : $"tunnel --url http://localhost:{localPort}";  // quick tunnel (temp URL)

        _log.LogInformation("Starting cloudflared: {Args}", args);

        _cloudflared = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        _cloudflared.ErrorDataReceived += OnCloudflaredOutput;
        _cloudflared.OutputDataReceived += OnCloudflaredOutput;
        _cloudflared.Exited += (_, _) =>
        {
            _log.LogWarning("cloudflared process exited");
            _running = false;
        };

        _cloudflared.Start();
        _cloudflared.BeginOutputReadLine();
        _cloudflared.BeginErrorReadLine();
        _running = true;
    }

    private void OnCloudflaredOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        _log.LogDebug("[cloudflared] {Line}", e.Data);

        // Parse the public URL from cloudflared quick tunnel output
        if (e.Data.Contains("trycloudflare.com") || e.Data.Contains(".cfargotunnel.com"))
        {
            var urlStart = e.Data.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
            if (urlStart >= 0)
            {
                var urlEnd = e.Data.IndexOfAny([' ', '\t', '\r', '\n'], urlStart);
                var url = urlEnd >= 0 ? e.Data[urlStart..urlEnd] : e.Data[urlStart..];
                if (url != PublicUrl)
                {
                    PublicUrl = url;
                    _log.LogInformation("Tunnel URL: {Url}", url);
                    UrlChanged?.Invoke(this, url);
                }
            }
        }
    }

    public void Stop()
    {
        if (_cloudflared is { HasExited: false })
        {
            try { _cloudflared.Kill(entireProcessTree: true); }
            catch { /* already dead */ }
        }
        _running = false;
    }

    private static string? FindCloudflared()
    {
        // 1. Check alongside this executable
        var local = Path.Combine(AppContext.BaseDirectory, "cloudflared.exe");
        if (File.Exists(local)) return local;

        // 2. Check PATH
        var fromPath = FindInPath("cloudflared.exe");
        if (fromPath is not null) return fromPath;

        return null;
    }

    private static string? FindInPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';'))
        {
            var full = Path.Combine(dir.Trim(), exe);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static string GetConfigPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DhurtaHost", "tunnel");
        return Path.Combine(dir, "config.yml");
    }

    public void Dispose()
    {
        Stop();
        _cloudflared?.Dispose();
    }
}
