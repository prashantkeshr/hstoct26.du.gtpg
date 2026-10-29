using System.Text.Json;
using System.Text.Json.Serialization;

namespace DhurtaHost.Protocols;

// ── Inbound (browser → agent) ──────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClientMessageType
{
    Auth,
    MouseMove, MouseButton, MouseScroll,
    KeyDown, KeyUp,
    ClipboardGet, ClipboardSet,
    WebRtcOffer, WebRtcAnswer, IceCandidate,
    Ping, Disconnect
}

public sealed class ClientMessage
{
    [JsonPropertyName("type")] public ClientMessageType Type { get; set; }

    // Auth
    [JsonPropertyName("token")] public string? Token { get; set; }

    // Mouse
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("button")] public string? Button { get; set; }    // "left"|"right"|"middle"
    [JsonPropertyName("down")] public bool Down { get; set; }           // true = press, false = release
    [JsonPropertyName("deltaX")] public double DeltaX { get; set; }
    [JsonPropertyName("deltaY")] public double DeltaY { get; set; }

    // Keyboard
    [JsonPropertyName("key")] public string? Key { get; set; }         // browser e.key value
    [JsonPropertyName("code")] public string? Code { get; set; }       // browser e.code value (physical position)
    [JsonPropertyName("keyCode")] public int KeyCode { get; set; }
    [JsonPropertyName("modifiers")] public string[]? Modifiers { get; set; }

    // Clipboard
    [JsonPropertyName("text")] public string? Text { get; set; }

    // WebRTC
    [JsonPropertyName("sdp")] public string? Sdp { get; set; }
    [JsonPropertyName("candidate")] public string? Candidate { get; set; }
    [JsonPropertyName("sdpMid")] public string? SdpMid { get; set; }
    [JsonPropertyName("sdpMLineIndex")] public int? SdpMLineIndex { get; set; }
}

// ── Outbound (agent → browser) ─────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServerMessageType
{
    AuthOk, AuthFail,
    ScreenInfo,
    Frame,                  // JPEG fallback (base64)
    WebRtcOffer, WebRtcAnswer, IceCandidate,
    ClipboardText,
    Pong, Error
}

public static class ServerMessage
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string AuthOk(string sessionId, int w, int h) =>
        Serialize(new { type = ServerMessageType.AuthOk, sessionId, screenWidth = w, screenHeight = h });

    public static string AuthFail(string reason) =>
        Serialize(new { type = ServerMessageType.AuthFail, reason });

    public static string ScreenInfo(int w, int h) =>
        Serialize(new { type = ServerMessageType.ScreenInfo, screenWidth = w, screenHeight = h });

    public static string Frame(string base64Jpeg) =>
        Serialize(new { type = ServerMessageType.Frame, data = base64Jpeg });

    public static string Pong() =>
        Serialize(new { type = ServerMessageType.Pong });

    public static string Error(string code, string message) =>
        Serialize(new { type = ServerMessageType.Error, code, message });

    public static string WebRtcOffer(string sdp, IEnumerable<object>? iceServers = null) =>
        Serialize(new { type = ServerMessageType.WebRtcOffer, sdp, iceServers });

    public static string WebRtcAnswer(string sdp) =>
        Serialize(new { type = ServerMessageType.WebRtcAnswer, sdp });

    public static string IceCandidate(string candidate, string sdpMid, int sdpMLineIndex) =>
        Serialize(new { type = ServerMessageType.IceCandidate, candidate, sdpMid, sdpMLineIndex });

    public static string ClipboardText(string text) =>
        Serialize(new { type = ServerMessageType.ClipboardText, text });

    private static string Serialize(object obj) =>
        JsonSerializer.Serialize(obj, Opts);
}
