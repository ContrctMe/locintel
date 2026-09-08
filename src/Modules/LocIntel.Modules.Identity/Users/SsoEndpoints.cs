using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Users;

public sealed record SsoStatusResponse(bool Available, bool Entitled);

public sealed record SsoPortalRequest(string Intent, string? ReturnPath);

public sealed record SsoPortalLinkResponse(string Url);

/// <summary>
/// Enterprise SSO self-service (ADR 41): the provider hosts the IT-admin
/// portal; we mint a link scoped to the org's external id. All three gates:
/// sso.enabled entitlement (402 upsell), org:manage grant, org-wide by nature.
/// </summary>
public static class SsoEndpoints
{
    [Transactional(
        typeof(IdentityDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/org/sso")]
    [ProducesResponseType(typeof(SsoStatusResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Status(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntitlements entitlements,
        IAuthProvider provider,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var entry = await db.OrgDirectory.FirstOrDefaultAsync(d => d.OrgId == org, ct);
        return Results.Ok(
            new SsoStatusResponse(
                Available: provider is IAdminPortal && entry?.ExternalId is not null,
                Entitled: await entitlements.HasAsync(org, EntitlementCatalog.SsoEnabled, ct)
            )
        );
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverinePost("/api/org/sso/portal")]
    [ProducesResponseType(typeof(SsoPortalLinkResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Portal(
        SsoPortalRequest request,
        HttpContext http,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntitlements entitlements,
        IAuthProvider provider,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        if (request.Intent is not ("sso" or "dsync"))
            return ApiErrors.BadRequest("intent must be 'sso' or 'dsync'");
        if (!await entitlements.HasAsync(org, EntitlementCatalog.SsoEnabled, ct))
            return GateResults.FeatureOff(EntitlementCatalog.SsoEnabled);

        var entry = await db.OrgDirectory.FirstOrDefaultAsync(d => d.OrgId == org, ct);
        if (provider is not IAdminPortal portal || entry?.ExternalId is not { } externalOrgId)
            return ApiErrors.NotFound("the auth provider has no admin portal");

        // open-redirect guard: relative paths only (same rule as auth returnUrl)
        var returnPath =
            request.ReturnPath is ['/', ..]
            && !request.ReturnPath.StartsWith("//")
            && !request.ReturnPath.Contains('\\')
                ? request.ReturnPath
                : "/";
        var url = await portal.GeneratePortalLinkAsync(
            externalOrgId,
            request.Intent == "dsync"
                ? AdminPortalIntent.DirectorySync
                : AdminPortalIntent.SingleSignOn,
            $"{http.Request.Scheme}://{http.Request.Host}{returnPath}",
            ct
        );
        return Results.Ok(new SsoPortalLinkResponse(url.ToString()));
    }
}
