using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Auth;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Access;

public sealed record ImpersonationResponse(DateTimeOffset ExpiresAt, string OrgName);

/// <summary>
/// Support impersonation (ADR 42): a time-boxed session into a tenant org,
/// carried entirely in the operator's own cookie - no synthetic memberships,
/// no database state to clean up. Start and stop are domain-audited into the
/// TARGET org so the tenant's own audit page shows support was there.
/// </summary>
public static class ImpersonationEndpoints
{
    [Transactional(typeof(IdentityDbContext))]
    [WolverinePost("/api/operator/orgs/{orgId}/impersonate")]
    [ProducesResponseType(typeof(ImpersonationResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Start(
        Guid orgId,
        HttpContext http,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        IConfiguration configuration,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            !await operators.IsOperatorAsync(accessor.Current, ct)
            || accessor.Current is not Principal.User { UserId: var operatorId }
        )
            return Results.Unauthorized();
        var target = new OrgId(orgId);
        var entry = await db.OrgDirectory.FirstOrDefaultAsync(d => d.OrgId == target, ct);
        if (entry is null)
            return Results.NotFound();
        if (entry.IsPlatform)
            return Results.BadRequest(new { error = "the platform org cannot be impersonated" });

        var ttl = configuration.GetValue<int?>("Impersonation:TtlSeconds") ?? 3600;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(ttl);
        var user = await db.Users.FirstAsync(u => u.Id == operatorId, ct);
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AuthEndpoints.BuildClaimsPrincipal(
                user,
                target,
                AuthEndpoints.GetSessionId(http.User),
                expiresAt
            )
        );
        await PublishAuditAsync(
            bus,
            target,
            "operator.impersonation.started",
            operatorId,
            user.Email,
            expiresAt
        );
        return Results.Ok(new ImpersonationResponse(expiresAt, entry.Name));
    }

    /// <summary>
    /// Reads the claim rather than the principal on purpose: an EXPIRED
    /// impersonation cookie must still find its way home.
    /// </summary>
    [Transactional(typeof(IdentityDbContext))]
    [WolverinePost("/auth/impersonation/stop")]
    public static async Task<IResult> Stop(
        HttpContext http,
        IdentityDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            http.User.FindFirst(LocIntelClaims.ImpersonationExpires) is null
            || !Guid.TryParse(http.User.FindFirst(LocIntelClaims.UserId)?.Value, out var userId)
        )
            return Results.NoContent();
        var target = Guid.TryParse(
            http.User.FindFirst(LocIntelClaims.ActiveOrg)?.Value,
            out var targetGuid
        )
            ? new OrgId(targetGuid)
            : (OrgId?)null;

        // back to the operator's real default org, same rule as login
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var homeOrg = await db
            .Memberships.Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            // UUIDv7 tie-break: CreatedAt collides at Postgres microsecond
            // resolution for memberships created together
            .ThenBy(m => m.Id)
            .Select(m => (OrgId?)m.OrgId)
            .FirstOrDefaultAsync(ct);
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AuthEndpoints.BuildClaimsPrincipal(user, homeOrg, AuthEndpoints.GetSessionId(http.User))
        );
        if (target is { } audited)
            await PublishAuditAsync(
                bus,
                audited,
                "operator.impersonation.ended",
                userId,
                user.Email,
                null
            );
        return Results.NoContent();
    }

    private static async Task PublishAuditAsync(
        IMessageBus bus,
        OrgId org,
        string action,
        Guid operatorId,
        string operatorEmail,
        DateTimeOffset? expiresAt
    ) =>
        await bus.PublishAsync(
            new RecordDomainAudit(
                action,
                System.Text.Json.JsonSerializer.Serialize(new { operatorEmail, expiresAt })
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = operatorId.ToString(),
                },
            }
        );
}
