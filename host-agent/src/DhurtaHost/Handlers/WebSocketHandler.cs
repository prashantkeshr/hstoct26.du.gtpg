using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DhurtaHost.Protocols;
using DhurtaHost.Services;

namespace DhurtaHost.Handlers;

public sealed class WebSocketHandler
{
    private const int AuthTimeoutSeconds = 8;
    private const int MaxMessageBytes = 65_536;
    private const int FrameChannelCapacity = 2;

    private readonly AuthService _auth;
    private readonly DatabaseService _db;
    private readonly RateLimiterService _limiter;
    private readonly ScreenCaptureService _capture;
    private readonly InputService _input;
    private readonly WebRtcService _webRtc;
    private readonly ILogger<WebSocketHandler> _log;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, WebSocket>
        _connected = new();

    public WebSocketHandler(
        AuthService auth, DatabaseService db, RateLimiterService limiter,
        ScreenCaptureService capture, InputService input, WebRtcService webRtc,
        ILogger<WebSocketHandler> log)
    {
        _auth = auth;
        _db = db;
        _limiter = limiter;
        _capture = capture;
        _input = input;
        _webRtc = webRtc;
        _log = log;
    }

    public async Task HandleAsync(WebSocket ws, CancellationToken ct)
    {
        string? sessionId = null;
        WebRtcSession? session = null;

        try
        {
            // ── Step 1: wait for auth within timeout ──────────────────────
            using var authCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            authCts.CancelAfter(TimeSpan.FromSeconds(AuthTimeoutSeconds));

            var firstMsg = await ReceiveTextAsync(ws, authCts.Token);
            if (firstMsg is null)
            {
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "No auth message", ct);
                return;
            }

            var msg = ParseMessage(firstMsg);
            if (msg is null || msg.Type != ClientMessageType.Auth || string.IsNullOrEmpty(msg.Token))
            {
                await SendTextAsync(ws, ServerMessage.AuthFail("Expected auth message"), ct);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth required", ct);
                return;
            }

            // ── Step 2: validate JWT ──────────────────────────────────────
            var principal = _auth.ValidateAccessToken(msg.Token);
            if (principal is null)
            {
                await SendTextAsync(ws, ServerMessage.AuthFail("Invalid or expired token"), ct);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", ct);
                return;
            }

            sessionId = _auth.GetSessionId(principal);
            var userId = _auth.GetUserId(principal);

            if (sessionId is null || userId is null)
            {
                await SendTextAsync(ws, ServerMessage.AuthFail("Malformed token"), ct);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", ct);
                return;
            }

            var dbSession = _db.GetSessionById(sessionId);
            if (dbSession is null || dbSession.ExpiresAt < DateTime.UtcNow)
            {
                await SendTextAsync(ws, ServerMessage.AuthFail("Session expired"), ct);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Session invalid", ct);
                return;
            }

            // ── Step 3: auth OK — send screen info ────────────────────────
            int w = _capture.ScreenWidth > 0 ? _capture.ScreenWidth : 1920;
            int h = _capture.ScreenHeight > 0 ? _capture.ScreenHeight : 1080;

            await SendTextAsync(ws, ServerMessage.AuthOk(sessionId, w, h), ct);
            await SendTextAsync(ws, ServerMessage.ScreenInfo(w, h), ct);

            _connected.TryAdd(sessionId, ws);
            _log.LogInformation("WebSocket session authenticated: {SessionId}", sessionId);

            // ── Step 4: create WebRTC session and send offer ───────────────
            session = await _webRtc.CreateSessionAsync(sessionId);

            // Unbounded channel for ICE candidates and other signaling messages
            var sigChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true,
            });
            session.SignalingMessageReady += sigMsg => sigChannel.Writer.TryWrite(sigMsg);

            string offerSdp = await session.InitAsync();
            await SendTextAsync(ws, ServerMessage.WebRtcOffer(offerSdp, _webRtc.IceServers), ct);

            // ── Step 5: frame routing ─────────────────────────────────────
            // While DataChannel is not open: queue JPEG in frameChannel → send as base64 over WS.
            // Once DataChannel opens: send binary JPEG directly over DataChannel (no WS overhead).
            var frameChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(FrameChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true,
            });

            void OnFrame(byte[] jpeg)
            {
                if (!session.SendFrame(jpeg))
                    frameChannel.Writer.TryWrite(jpeg);
            }
            _capture.FrameReady += OnFrame;

            // ── Step 6: run send + receive loops concurrently ─────────────
            using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var sendLoop = SendLoopAsync(ws, frameChannel.Reader, sigChannel.Reader, loopCts.Token);
            var recvLoop = ReceiveLoopAsync(ws, sessionId, session, loopCts.Token);

            await Task.WhenAny(sendLoop, recvLoop);
            loopCts.Cancel();

            _capture.FrameReady -= OnFrame;
            frameChannel.Writer.TryComplete();
            sigChannel.Writer.TryComplete();

            await Task.WhenAll(
                sendLoop.ContinueWith(_ => { }, TaskContinuationOptions.None),
                recvLoop.ContinueWith(_ => { }, TaskContinuationOptions.None));
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely) { }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WebSocket error for session {SessionId}", sessionId);
        }
        finally
        {
            session?.Dispose();
            if (sessionId is not null) _connected.TryRemove(sessionId, out _);
            if (ws.State != WebSocketState.Closed && ws.State != WebSocketState.Aborted)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None); }
                catch { }
            }
            _log.LogInformation("WebSocket session closed: {SessionId}", sessionId ?? "unauthenticated");
        }
    }

    // ── Combined send loop: drains signaling first, then frames ──────────────────

    private static async Task SendLoopAsync(
        WebSocket ws,
        ChannelReader<byte[]> frames,
        ChannelReader<string> signaling,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                // Drain all pending signaling messages first (ICE candidates have priority)
                while (signaling.TryRead(out var sigMsg))
                    await SendTextAsync(ws, sigMsg, ct);

                // Send one JPEG frame over WS if available (DataChannel fallback path)
                if (frames.TryRead(out var jpeg))
                {
                    await SendTextAsync(ws, ServerMessage.Frame(Convert.ToBase64String(jpeg)), ct);
                    continue;
                }

                // Nothing ready — wait for either channel to have data
                var sigWait = signaling.WaitToReadAsync(ct).AsTask();
                var frameWait = frames.WaitToReadAsync(ct).AsTask();
                await Task.WhenAny(sigWait, frameWait);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex) when (ex is ChannelClosedException or ObjectDisposedException) { }
    }

    // ── Receive loop ──────────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(
        WebSocket ws, string sessionId, WebRtcSession? session, CancellationToken ct)
    {
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var text = await ReceiveTextAsync(ws, ct);
                if (text is null) break;

                var clientMsg = ParseMessage(text);
                if (clientMsg is null) continue;

                await RouteMessageAsync(ws, clientMsg, sessionId, session, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    // ── Message routing ───────────────────────────────────────────────────────────

    private async Task RouteMessageAsync(
        WebSocket ws, ClientMessage msg, string sessionId,
        WebRtcSession? session, CancellationToken ct)
    {
        switch (msg.Type)
        {
            case ClientMessageType.Ping:
                await SendTextAsync(ws, ServerMessage.Pong(), ct);
                break;

            case ClientMessageType.Disconnect:
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", ct);
                break;

            // ── WebRTC signaling ───────────────────────────────────────────
            case ClientMessageType.WebRtcAnswer:
                if (session is not null && msg.Sdp is not null)
                {
                    session.SetAnswer(msg.Sdp);
                    _log.LogDebug("WebRTC answer applied for {SessionId}", sessionId);
                }
                break;

            case ClientMessageType.IceCandidate:
                if (session is not null && msg.Candidate is not null)
                    session.AddIceCandidate(msg.Candidate, msg.SdpMid, msg.SdpMLineIndex);
                break;

            // ── Input events ───────────────────────────────────────────────
            case ClientMessageType.MouseMove:
                _input.MouseMove(msg.X, msg.Y);
                break;

            case ClientMessageType.MouseButton:
                _input.MouseButton(msg.Button ?? "left", msg.Down);
                break;

            case ClientMessageType.MouseScroll:
                _input.MouseScroll(msg.DeltaX, msg.DeltaY);
                break;

            case ClientMessageType.KeyDown:
                _input.KeyEvent(msg.Key, msg.Code, down: true);
                break;

            case ClientMessageType.KeyUp:
                _input.KeyEvent(msg.Key, msg.Code, down: false);
                break;

            case ClientMessageType.ClipboardGet:
            case ClientMessageType.ClipboardSet:
                await SendTextAsync(ws, ServerMessage.Error("NOT_IMPLEMENTED", "Clipboard in Phase 5"), ct);
                break;

            default:
                await SendTextAsync(ws, ServerMessage.Error("UNKNOWN_MESSAGE", $"Unknown: {msg.Type}"), ct);
                break;
        }
    }

    // ── I/O helpers ───────────────────────────────────────────────────────────────

    private static async Task<string?> ReceiveTextAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[MaxMessageBytes];
        var ms = new MemoryStream();

        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) return null;

            ms.Write(buffer, 0, result.Count);
            if (ms.Length > MaxMessageBytes) return null;
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    private static async Task SendTextAsync(WebSocket ws, string text, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static ClientMessage? ParseMessage(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<ClientMessage>(text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }
}
