using DhurtaHost.Services;

namespace DhurtaHost.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly TunnelService _tunnel;
    private readonly IHost _host;
    private readonly CancellationTokenSource _cts;

    public TrayApplicationContext(IHost host, IServiceProvider services, CancellationTokenSource cts)
    {
        _host = host;
        _cts = cts;
        _tunnel = services.GetRequiredService<TunnelService>();

        _tray = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Dhurta Host — Starting...",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };

        _tunnel.UrlChanged += (_, url) =>
        {
            _tray.Text = $"Dhurta Host — {TruncateUrl(url)}";
            _tray.BalloonTipTitle = "Dhurta Host";
            _tray.BalloonTipText = $"Tunnel active: {url}";
            _tray.ShowBalloonTip(3000);
        };

        if (_tunnel.PublicUrl is not null)
            _tray.Text = $"Dhurta Host — {TruncateUrl(_tunnel.PublicUrl)}";
        else
            _tray.Text = "Dhurta Host — Online";
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var statusItem = new ToolStripMenuItem("Dhurta Host") { Enabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());

        var urlItem = new ToolStripMenuItem("Copy tunnel URL");
        urlItem.Click += (_, _) =>
        {
            if (_tunnel.PublicUrl is not null)
                Clipboard.SetText(_tunnel.PublicUrl);
        };
        menu.Items.Add(urlItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit Dhurta Host");
        exitItem.Click += (_, _) => ExitGracefully();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ExitGracefully()
    {
        _tray.Visible = false;
        _cts.Cancel();
        Application.Exit();
    }

    private static Icon LoadIcon()
    {
        // Use a built-in Windows icon as fallback when no custom icon is embedded
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "icon.ico");
            if (File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch { /* fall through to default */ }

        return SystemIcons.Application;
    }

    private static string TruncateUrl(string url) =>
        url.Length > 40 ? url[..37] + "..." : url;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tray.Dispose();
        base.Dispose(disposing);
    }
}
