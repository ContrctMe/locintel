using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Auth;

namespace LocIntel.Api;

/// <summary>
/// A browser's last observed session is a precondition, not authority. A shared
/// cookie may change in another tab; never execute stale intent under that cookie.
/// API-key clients and callers without this optional browser precondition retain
/// their existing authentication contract.
/// </summary>
public sealed class SessionContextMiddleware(RequestDelegate next)
{
    public const string Header = "X-LocIntel-Session-Context";

    public async Task InvokeAsync(HttpContext context)
    {
        var claims = new[]
        {
            LocIntelClaims.Tier,
            LocIntelClaims.UserId,
            LocIntelClaims.ContactId,
            LocIntelClaims.ActiveOrg,
            LocIntelClaims.SessionId,
            LocIntelClaims.ImpersonationExpires,
        }
            .Select(name => context.User.FindFirst(name)?.Value)
            .ToArray();
        // Expose only a fingerprint, never the session identifier or cookie.
        var current = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims)))
        );
        if (context.Request.Path == "/me")
            context.Response.Headers[Header] = current;
        else if (
            context.Request.Headers.TryGetValue(Header, out var expected)
            && !string.Equals(expected.ToString(), current, StringComparison.Ordinal)
        )
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(
                ApiErrors.Body(
                    "Your session changed in another tab. Reload before continuing.",
                    "session_context_changed"
                ),
                context.RequestAborted
            );
            return;
        }
        await next(context);
    }
}
