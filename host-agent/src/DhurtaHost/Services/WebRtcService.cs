using SIPSorcery.Net;
using DhurtaHost.Protocols;

namespace DhurtaHost.Services;

/// <summary>
/// Manages a single WebRTC P2P session. The host agent is always the offerer.
/// The "screen" DataChannel carries raw JPEG bytes — no JSON overhead.
/// </summary>
public sealed class WebRtcSession : IDisposable
{
    private readonly RTCPeerConnection _pc;
    private RTCDataChannel? _dc;
    private volatile bool _dcOpen;
    private bool _disposed;
    private readonly ILogger _log;

    public string SessionId { get; }

    /// Fires with a serialised JSON string ready to forward to the browser (ICE candidates).
    public event Action<string>? SignalingMessageReady;

    /// Fires when the DataChannel opens — route frames here instead of the WebSocket.
    public event Action? DataChannelOpened;

    /// Fires when the DataChannel closes — fall back to WebSocket frame delivery.
    public event Action? DataChannelClosed;

    public bool IsDataChannelOpen => _dcOpen;

    internal WebRtcSession(string sessionId, RTCPeerConnection pc, RTCDataChannel dc, ILogger log)
    {
        SessionId = sessionId;
        _pc = pc;
        _dc = dc;
        _log = log;

        _pc.onicecandidate += OnIceCandidateEvent;
        _pc.onconnectionstatechange += state =>
            _log.LogDebug("WebRTC {SessionId} → {State}", sessionId, state);

        _dc.onopen += OnDcOpen;
        _dc.onclose += OnDcClose;
    }

    /// Creates the SDP offer and starts ICE gathering. Returns the offer SDP.
    public async Task<string> InitAsync()
    {
        var offer = _pc.createOffer(null);
        await _pc.setLocalDescription(offer);
        return offer.sdp;
    }

    /// Sets the browser's SDP answer.
    public void SetAnswer(string sdp)
    {
        var result = _pc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.answer,
            sdp = sdp,
        });
        if (result != SetDescriptionResultEnum.OK)
            _log.LogWarning("setRemoteDescription failed for {SessionId}: {Result}", SessionId, result);
    }

    /// Adds an ICE candidate received from the browser.
    public void AddIceCandidate(string candidate, string? sdpMid, int? sdpMLineIndex)
    {
        _pc.addIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid ?? "0",
            sdpMLineIndex = (ushort)(sdpMLineIndex ?? 0),
        });
    }

    /// Sends a JPEG frame over the DataChannel. Returns false when the channel is not open.
    public bool SendFrame(byte[] jpeg)
    {
        if (!_dcOpen || _dc is null) return false;
        try { _dc.send(jpeg); return true; }
        catch (Exception ex) { _log.LogDebug(ex, "DataChannel send failed"); return false; }
    }

    private void OnIceCandidateEvent(RTCIceCandidate candidate)
    {
        if (string.IsNullOrEmpty(candidate?.candidate)) return;
        SignalingMessageReady?.Invoke(
            ServerMessage.IceCandidate(
                candidate.candidate,
                candidate.sdpMid ?? "0",
                (int)candidate.sdpMLineIndex));
    }

    private void OnDcOpen()
    {
        _dcOpen = true;
        _log.LogInformation("DataChannel open for session {SessionId}", SessionId);
        DataChannelOpened?.Invoke();
    }

    private void OnDcClose()
    {
        _dcOpen = false;
        _log.LogInformation("DataChannel closed for session {SessionId}", SessionId);
        DataChannelClosed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dcOpen = false;
        _dc = null;
        _pc.onicecandidate -= OnIceCandidateEvent;
        try { _pc.close(); } catch { }
    }
}

/// Creates WebRTC sessions. Stateless — each session owns its own RTCPeerConnection.
public sealed class WebRtcService
{
    private readonly RTCConfiguration _rtcConfig;
    private readonly ILogger<WebRtcService> _log;

    /// ICE servers in JSON-serialisable form to embed in the WebRtcOffer message for the browser.
    public IReadOnlyList<object> IceServers { get; }

    public WebRtcService(ILogger<WebRtcService> log, IConfiguration config)
    {
        _log = log;

        var sipServers = new List<RTCIceServer>
        {
            new() { urls = "stun:stun.l.google.com:19302" },
            new() { urls = "stun:stun.cloudflare.com:3478" },
        };

        var turnUrl = config["Turn:Url"] ?? "";
        if (!string.IsNullOrEmpty(turnUrl))
        {
            sipServers.Add(new RTCIceServer
            {
                urls = turnUrl,
                username = config["Turn:Username"] ?? "dhurta",
                credential = config["Turn:Credential"] ?? "",
            });
            _log.LogInformation("TURN relay configured: {Url}", turnUrl);
        }

        _rtcConfig = new RTCConfiguration { iceServers = sipServers };

        // Build JSON-serialisable list for the browser (serialised with CamelCase policy)
        IceServers = sipServers.Select(s =>
        {
            if (string.IsNullOrEmpty(s.username))
                return (object)new { urls = s.urls };
            return (object)new { urls = s.urls, username = s.username, credential = s.credential };
        }).ToList();
    }

    public async Task<WebRtcSession> CreateSessionAsync(string sessionId)
    {
        var pc = new RTCPeerConnection(_rtcConfig);
        var dc = await pc.createDataChannel("screen");
        _log.LogDebug("WebRTC session created: {SessionId}", sessionId);
        return new WebRtcSession(sessionId, pc, dc, _log);
    }
}
