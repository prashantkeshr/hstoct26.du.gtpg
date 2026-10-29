using DhurtaHost.Services;

namespace DhurtaHost.Middleware;

public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimiterService _limiter;

    public RateLimitMiddleware(RequestDelegate next, RateLimiterService limiter)
    {
        _next = next;
        _limiter = limiter;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Respect CF-Connecting-IP from Cloudflare Tunnel
        var ip = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                 ?? ctx.Connection.RemoteIpAddress?.ToString()
                 ?? "unknown";

        var path = ctx.Request.Path.Value ?? "";

        // Tighter limit for auth endpoints
        var action = path.StartsWith("/api/auth") ? "auth" : "general";

        if (action == "auth" && _limiter.IsAuthLocked(ip))
        {
            ctx.Response.StatusCode = 429;
            ctx.Response.Headers["Retry-After"] = "900";
            await ctx.Response.WriteAsJsonAsync(new { error = "Too many failed attempts. Try again in 15 minutes." });
            return;
        }

        if (!_limiter.Allow(ip, action))
        {
            ctx.Response.StatusCode = 429;
            ctx.Response.Headers["Retry-After"] = "60";
            await ctx.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded." });
            return;
        }

        await _next(ctx);
    }
}
