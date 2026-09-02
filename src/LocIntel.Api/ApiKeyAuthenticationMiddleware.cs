using System.Security.Cryptography;
using System.Text;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Api;

/// <summary>
/// Server-to-server authentication (ADR 40): "Authorization: Bearer
/// locintel_..." resolves by SHA-256 hash to an unrevoked ApiKey, making the
/// request a SERVICE principal of the key's org. This middleware owns the one
/// DB lookup (like GuestOrgMiddleware); the accessor reads HttpContext.Items.
/// api_keys is platform-global, so the read needs no tenant. LastUsedAt is
/// throttled to one write per key per five minutes.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware(RequestDelegate next)
{
    public const string TokenPrefix = "locintel_";

    public async Task InvokeAsync(HttpContext context, IdentityDbContext db)
    {
        if (
            context.User.Identity?.IsAuthenticated != true
            && context.Request.Headers.Authorization.FirstOrDefault() is { } header
            && header.StartsWith("Bearer " + TokenPrefix, StringComparison.Ordinal)
        )
        {
            var token = header["Bearer ".Length..];
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            var now = DateTimeOffset.UtcNow;
            var key = await db.ApiKeys.FirstOrDefaultAsync(
                k =>
                    k.SecretHash == hash
                    && k.RevokedAt == null
                    && (k.ExpiresAt == null || k.ExpiresAt > now),
                context.RequestAborted
            );
            if (key is null)
            {
                // a presented-but-invalid credential is a hard 401, never a
                // silent fall-through to the guest tier
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "invalid api key" });
                return;
            }
            context.Items[RequestPrincipalAccessor.ServiceKeyItem] = (key.Id, key.OrgId);
            if (
                key.LastUsedAt is null
                || DateTimeOffset.UtcNow - key.LastUsedAt > TimeSpan.FromMinutes(5)
            )
            {
                key.LastUsedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(context.RequestAborted);
            }
        }
        await next(context);
    }
}
