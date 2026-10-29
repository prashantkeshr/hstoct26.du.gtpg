using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace DhurtaHost.Services;

/// <summary>
/// Captures the primary display at a configurable frame rate and fires
/// FrameReady with JPEG-encoded bytes. Skips identical frames to save
/// bandwidth when the desktop is static.
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    private readonly ILogger<ScreenCaptureService> _log;
    private readonly object _lock = new();

    private int _targetFps = 15;
    private int _jpegQuality = 70;
    private System.Threading.Timer? _timer;
    private ImageCodecInfo? _jpegCodec;
    private EncoderParameters? _encoderParams;
    private byte[]? _lastFrameHash;
    private bool _disposed;

    public event Action<byte[]>? FrameReady;

    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }

    public ScreenCaptureService(ILogger<ScreenCaptureService> log)
    {
        _log = log;
    }

    public void Configure(int fps = 15, int jpegQuality = 70)
    {
        _targetFps = Math.Clamp(fps, 1, 30);
        _jpegQuality = Math.Clamp(jpegQuality, 20, 95);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_timer is not null) return;

            // Sample screen size once (full virtual screen spanning all monitors)
            ScreenWidth = SystemInformation.VirtualScreen.Width;
            ScreenHeight = SystemInformation.VirtualScreen.Height;

            // Cache JPEG codec + quality parameter
            _jpegCodec = GetJpegCodec();
            _encoderParams = new EncoderParameters(1);
            _encoderParams.Param[0] = new EncoderParameter(
                Encoder.Quality, (long)_jpegQuality);

            var interval = TimeSpan.FromMilliseconds(1000.0 / _targetFps);
            _timer = new System.Threading.Timer(CaptureFrame, null, TimeSpan.Zero, interval);

            _log.LogInformation(
                "Screen capture started: {W}x{H} @ {Fps} fps, JPEG q{Q}",
                ScreenWidth, ScreenHeight, _targetFps, _jpegQuality);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void CaptureFrame(object? _)
    {
        if (FrameReady is null) return; // no subscribers — skip work

        try
        {
            int w = ScreenWidth;
            int h = ScreenHeight;

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(
                SystemInformation.VirtualScreen.Left,
                SystemInformation.VirtualScreen.Top,
                0, 0,
                new Size(w, h),
                CopyPixelOperation.SourceCopy);

            // Encode to JPEG
            using var ms = new MemoryStream(capacity: w * h / 4);
            bmp.Save(ms, _jpegCodec!, _encoderParams!);
            var jpeg = ms.ToArray();

            // Skip identical frames (desktop static)
            var hash = MD5.HashData(jpeg);
            if (_lastFrameHash is not null && hash.AsSpan().SequenceEqual(_lastFrameHash))
                return;

            _lastFrameHash = hash;
            FrameReady?.Invoke(jpeg);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Frame capture failed");
        }
    }

    private static ImageCodecInfo GetJpegCodec()
    {
        foreach (var c in ImageCodecInfo.GetImageEncoders())
            if (c.FormatID == ImageFormat.Jpeg.Guid)
                return c;
        throw new InvalidOperationException("JPEG codec not found");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _encoderParams?.Dispose();
    }
}
