using DhurtaHost.Models;
using DhurtaHost.Services;

namespace DhurtaHost.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        // ── Setup: create owner account (first-run only) ──────────────────
        group.MapPost("/setup", async (SetupRequest req, DatabaseService db,
            AuthService auth, DeviceService device, HttpContext ctx) =>
        {
            if (db.HasOwner())
                return Results.Conflict(new { error = "Owner already configured." });

            if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length > 64)
                return Results.BadRequest(new { error = "Invalid username." });

            if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 12)
                return Results.BadRequest(new { error = "Password must be at least 12 characters." });

            var user = new User
            {
                Id = AuthService.NewId(),
                Username = req.Username.Trim().ToLowerInvariant(),
                PasswordHash = auth.HashPassword(req.Password),
                CreatedAt = DateTime.UtcNow,
                IsOwner = true,
            };
            db.InsertUser(user);

            return Results.Ok(new { message = "Owner account created. You can now log in." });
        });

        // ── Login ─────────────────────────────────────────────────────────
        group.MapPost("/login", async (LoginRequest req, DatabaseService db,
            AuthService auth, DeviceService device, RateLimiterService limiter, HttpContext ctx) =>
        {
            var ip = GetClientIp(ctx);

            if (limiter.IsAuthLocked(ip))
                return Results.StatusCode(429);

            if (string.IsNullOrEmpty(req.Username) || string.IsNullOrEmpty(req.Password))
                return AuthFailure(limiter, ip);

            var user = db.GetUserByUsername(req.Username.Trim().ToLowerInvariant());
            if (user is null || !auth.VerifyPassword(req.Password, user.PasswordHash))
                return AuthFailure(limiter, ip);

            var (accessToken, refreshToken, session) =
                auth.IssueTokens(user, device.Self.Id, ip);
            db.InsertSession(session);

            return Results.Ok(new
            {
                accessToken,
                refreshToken,
                expiresIn = 900,       // 15 min in seconds
                sessionId = session.Id,
            });
        });

        // ── Login with access key ─────────────────────────────────────────
        group.MapPost("/login-key", async (AccessKeyLoginRequest req, DatabaseService db,
            AuthService auth, DeviceService device, RateLimiterService limiter, HttpContext ctx) =>
        {
            var ip = GetClientIp(ctx);

            if (limiter.IsAuthLocked(ip))
                return Results.StatusCode(429);

            if (string.IsNullOrEmpty(req.Key))
                return AuthFailure(limiter, ip);

            var activeKeys = db.GetActiveAccessKeys(device.Self.Id);
            AccessKey? matchedKey = null;

            foreach (var k in activeKeys)
            {
                if (auth.VerifyAccessKey(req.Key, k))
                {
                    matchedKey = k;
                    break;
                }
            }

            if (matchedKey is null)
                return AuthFailure(limiter, ip);

            db.TouchAccessKey(matchedKey.Id);

            // Access keys authenticate as the device owner
            var owner = db.GetUser(matchedKey.DeviceId) ??
                        db.GetUserByUsername("owner");  // fallback to first owner

            if (owner is null)
                return Results.StatusCode(500);

            var (accessToken, refreshToken, session) =
                auth.IssueTokens(owner, device.Self.Id, ip);
            db.InsertSession(session);

            return Results.Ok(new
            {
                accessToken,
                refreshToken,
                expiresIn = 900,
                sessionId = session.Id,
            });
        });

        // ── Refresh ───────────────────────────────────────────────────────
        group.MapPost("/refresh", async (RefreshRequest req, DatabaseService db,
            AuthService auth, DeviceService device, HttpContext ctx) =>
        {
            var ip = GetClientIp(ctx);

            if (string.IsNullOrEmpty(req.RefreshToken))
                return Results.Unauthorized();

            // Validate current access token (even if expired, get session id)
            var principal = auth.ValidateAccessToken(req.AccessToken ?? "");
            var sessionId = principal is not null ? auth.GetSessionId(principal) : null;

            if (sessionId is null) return Results.Unauthorized();

            var session = db.GetSessionById(sessionId);
            if (session is null || session.ExpiresAt < DateTime.UtcNow)
                return Results.Unauthorized();

            // Revoke old session, issue new one
            db.RevokeSession(sessionId);

            var user = db.GetUser(session.UserId);
            if (user is null) return Results.Unauthorized();

            var (newAccessToken, newRefreshToken, newSession) =
                auth.IssueTokens(user, device.Self.Id, ip);
            db.InsertSession(newSession);

            return Results.Ok(new
            {
                accessToken = newAccessToken,
                refreshToken = newRefreshToken,
                expiresIn = 900,
                sessionId = newSession.Id,
            });
        });

        // ── Logout ────────────────────────────────────────────────────────
        group.MapPost("/logout", (LogoutRequest req, DatabaseService db, AuthService auth) =>
        {
            if (!string.IsNullOrEmpty(req.SessionId))
                db.RevokeSession(req.SessionId);
            return Results.Ok(new { message = "Logged out." });
        });

        // ── Generate access key ───────────────────────────────────────────
        group.MapPost("/keys", (CreateKeyRequest req, DatabaseService db,
            AuthService auth, DeviceService device, HttpContext ctx) =>
        {
            var principal = ValidateRequest(ctx, auth, db);
            if (principal is null) return Results.Unauthorized();

            var (plainKey, record) = auth.GenerateAccessKey(
                device.Self.Id,
                req.Label ?? "Unnamed key",
                req.ExpiresAt);
            db.InsertAccessKey(record);

            return Results.Ok(new
            {
                id = record.Id,
                key = plainKey,
                label = record.Label,
                expiresAt = record.ExpiresAt,
                message = "Save this key — it will not be shown again."
            });
        });

        // ── List access keys ──────────────────────────────────────────────
        group.MapGet("/keys", (DatabaseService db, AuthService auth,
            DeviceService device, HttpContext ctx) =>
        {
            var principal = ValidateRequest(ctx, auth, db);
            if (principal is null) return Results.Unauthorized();

            var keys = db.GetActiveAccessKeys(device.Self.Id)
                .Select(k => new
                {
                    id = k.Id,
                    label = k.Label,
                    createdAt = k.CreatedAt,
                    expiresAt = k.ExpiresAt,
                    lastUsedAt = k.LastUsedAt,
                });
            return Results.Ok(keys);
        });

        // ── Revoke access key ─────────────────────────────────────────────
        group.MapDelete("/keys/{id}", (string id, DatabaseService db,
            AuthService auth, HttpContext ctx) =>
        {
            var principal = ValidateRequest(ctx, auth, db);
            if (principal is null) return Results.Unauthorized();

            db.RevokeAccessKey(id);
            return Results.Ok(new { message = "Key revoked." });
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IResult AuthFailure(RateLimiterService limiter, string ip)
    {
        limiter.RecordAuthFailure(ip);
        return Results.Unauthorized();
    }

    private static System.Security.Claims.ClaimsPrincipal? ValidateRequest(
        HttpContext ctx, AuthService auth, DatabaseService db)
    {
        var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader is null || !authHeader.StartsWith("Bearer ")) return null;
        var token = authHeader["Bearer ".Length..];
        var principal = auth.ValidateAccessToken(token);
        if (principal is null) return null;
        var sessionId = auth.GetSessionId(principal);
        if (sessionId is null) return null;
        var session = db.GetSessionById(sessionId);
        return session is null || session.ExpiresAt < DateTime.UtcNow ? null : principal;
    }

    private static string GetClientIp(HttpContext ctx) =>
        ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
        ?? ctx.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record SetupRequest(string Username, string Password);
public record LoginRequest(string Username, string Password);
public record AccessKeyLoginRequest(string Key);
public record RefreshRequest(string? AccessToken, string RefreshToken);
public record LogoutRequest(string? SessionId);
public record CreateKeyRequest(string? Label, DateTime? ExpiresAt);
