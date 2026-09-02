using System.Security.Claims;
using LocIntel.Modules.Identity.Auth;
using LocIntel.Modules.Identity.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Api;

/// <summary>
/// The cookie is self-contained; the session RECORD is the revocation
/// authority. A user cookie whose session is revoked or missing is treated as
/// signed out - the request continues as an anonymous guest, so the normal
/// gates produce the 401s. Contact and guest tiers carry no session claim and
/// pass through untouched. user_sessions is platform-global, so this read
/// needs no tenant - same class of per-request read as SuspensionMiddleware.
/// </summary>
public sealed class SessionValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IdentityDbContext db)
    {
        if (context.User.FindFirstValue(LocIntelClaims.Tier) == "user")
        {
            var valid =
                Guid.TryParse(
                    context.User.FindFirstValue(LocIntelClaims.SessionId),
                    out var sessionId
                )
                && await db.Sessions.AnyAsync(
                    s => s.Id == sessionId && s.RevokedAt == null,
                    context.RequestAborted
                );
            if (!valid)
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
            }
        }
        await next(context);
    }
}
