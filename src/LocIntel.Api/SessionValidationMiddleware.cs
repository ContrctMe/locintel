using System.Security.Claims;
using LocIntel.Modules.Identity.Auth;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
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
            var oldest = DateTimeOffset.UtcNow.AddHours(-12);
            var userId = context.User.FindFirstValue(LocIntelClaims.UserId);
            var valid =
                Guid.TryParse(
                    context.User.FindFirstValue(LocIntelClaims.SessionId),
                    out var sessionId
                )
                && await db.Sessions.AnyAsync(
                    s =>
                        s.Id == sessionId
                        && s.RevokedAt == null
                        && s.CreatedAt > oldest
                        && s.UserId.ToString() == userId,
                    context.RequestAborted
                );
            if (valid && context.User.HasClaim(c => c.Type == LocIntelClaims.ImpersonationExpires))
            {
                valid = false;
                var id = Guid.Parse(userId!);
                var platformOrgs = await (
                    from m in db.Memberships
                    join org in db.OrgDirectory on m.OrgId equals org.OrgId
                    where m.UserId == id && org.IsPlatform
                    select org.OrgId
                ).ToListAsync(context.RequestAborted);
                foreach (var org in platformOrgs)
                {
                    await using var scope = context.RequestServices.CreateAsyncScope();
                    scope
                        .ServiceProvider.GetRequiredService<TenantContext>()
                        .Set(org, RegionId.Default);
                    var resolver = scope.ServiceProvider.GetRequiredService<IScopeResolver>();
                    if (
                        await resolver.CanAsync(
                            new Principal.User(id, "", null, org),
                            Capabilities.PlatformOperate,
                            context.RequestAborted
                        )
                    )
                    {
                        valid = true;
                        break;
                    }
                }
            }
            if (!valid)
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
            }
        }
        await next(context);
    }
}
