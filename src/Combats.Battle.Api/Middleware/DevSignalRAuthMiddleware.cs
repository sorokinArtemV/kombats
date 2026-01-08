using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Combats.Battle.Api.Middleware;

/// <summary>
/// DEV-ONLY: Middleware that substitutes JWT authentication for SignalR in Development environment.
/// Reads X-Player-Id header or playerId query parameter and creates a ClaimsPrincipal with sub claim.
/// This middleware is ONLY active in Development environment.
/// In Production, proper JWT authentication should be used.
/// </summary>
public class DevSignalRAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DevSignalRAuthMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public DevSignalRAuthMiddleware(
        RequestDelegate next,
        ILogger<DevSignalRAuthMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply in Development environment
        if (!_environment.IsDevelopment())
        {
            await _next(context);
            return;
        }

        // Only apply to SignalR endpoints
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/battlehub", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Try to get playerId from header or query string
        var playerIdStr = context.Request.Headers["X-Player-Id"].FirstOrDefault()
                         ?? context.Request.Query["playerId"].FirstOrDefault();

        if (!string.IsNullOrEmpty(playerIdStr) && Guid.TryParse(playerIdStr, out var playerId))
        {
            // Create a ClaimsPrincipal with sub claim
            var claims = new[]
            {
                new Claim("sub", playerId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, playerId.ToString())
            };

            var identity = new ClaimsIdentity(claims, "DevAuth");
            context.User = new ClaimsPrincipal(identity);

            _logger.LogDebug(
                "DEV: Authenticated SignalR connection with playerId {PlayerId} via dev auth middleware",
                playerId);
        }
        else
        {
            _logger.LogWarning(
                "DEV: SignalR connection without X-Player-Id header or playerId query parameter. Connection will fail if [Authorize] is required.");
        }

        await _next(context);
    }
}





