using DhurtaHost.Services;

namespace DhurtaHost.Api;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/device");

        group.MapGet("/", (DeviceService device, DatabaseService db,
            AuthService auth, ScreenCaptureService capture, HttpContext ctx) =>
        {
            var principal = ValidateRequest(ctx, auth, db);
            if (principal is null) return Results.Unauthorized();

            var d = device.Self;
            return Results.Ok(new
            {
                id = d.Id,
                name = d.Name,
                os = d.OsVersion,
                osVersion = d.OsVersion,
                agentVersion = d.AgentVersion,
                screenWidth = capture.ScreenWidth > 0 ? capture.ScreenWidth : 1920,
                screenHeight = capture.ScreenHeight > 0 ? capture.ScreenHeight : 1080,
                lastSeenAt = d.LastSeenAt,
                unattendedAccess = d.UnattendedAccessEnabled,
                status = "online",
            });
        });

        group.MapPatch("/unattended", (UnattendedRequest req, DeviceService device,
            DatabaseService db, AuthService auth, HttpContext ctx) =>
        {
            var principal = ValidateRequest(ctx, auth, db);
            if (principal is null) return Results.Unauthorized();

            device.SetUnattendedAccess(req.Enabled);
            return Results.Ok(new { unattendedAccess = req.Enabled });
        });

        group.MapGet("/info", (HttpContext ctx) =>
        {
            // Public endpoint: tells the client what auth modes are available
            var db = ctx.RequestServices.GetRequiredService<DatabaseService>();
            return Results.Ok(new
            {
                hasOwner = db.HasOwner(),
                agentVersion = typeof(DeviceEndpoints).Assembly.GetName().Version?.ToString(3),
            });
        });
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
}

public record UnattendedRequest(bool Enabled);
