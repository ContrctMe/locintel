using LocIntel.Modules.Identity.Auth;
using LocIntel.Platform.Kernel;

namespace LocIntel.Api;

/// <summary>
/// Read-time principal resolution (step-1 lesson): singleton over
/// IHttpContextAccessor so the answer is available from any DI scope at any
/// point in the pipeline - including when Wolverine's transactional middleware
/// opens a connection before request middleware has run. Reads claims and
/// request state only; never the database (guest org is materialized into
/// HttpContext.Items by GuestOrgMiddleware, which owns the one DB lookup).
/// </summary>
public sealed class RequestPrincipalAccessor(IHttpContextAccessor accessor) : IPrincipalAccessor
{
    public const string GuestOrgItem = "locintel.guest_org";
    public const string ServiceKeyItem = "locintel.service_key";

    public Principal Current
    {
        get
        {
            var http = accessor.HttpContext;
            if (http is null)
                return new Principal.Guest(null);

            // a validated API key (ApiKeyAuthenticationMiddleware owns the
            // lookup) makes this a SERVICE principal - server-to-server
            if (
                http.Items.TryGetValue(ServiceKeyItem, out var serviceRaw)
                && serviceRaw is (Guid serviceKeyId, OrgId serviceOrg)
            )
                return new Principal.Service(serviceKeyId, serviceOrg);

            var user = http.User;
            if (
                user.Identity?.IsAuthenticated == true
                && user.FindFirst(LocIntelClaims.Tier)?.Value == "contact"
                && Guid.TryParse(user.FindFirst("locintel:contact_id")?.Value, out var contactId)
                && Guid.TryParse(
                    user.FindFirst(LocIntelClaims.ActiveOrg)?.Value,
                    out var contactOrg
                )
            )
            {
                return new Principal.Contact(
                    contactId,
                    new OrgId(contactOrg),
                    user.FindFirst(LocIntelClaims.Email)?.Value
                );
            }

            if (
                user.Identity?.IsAuthenticated == true
                && Guid.TryParse(user.FindFirst(LocIntelClaims.UserId)?.Value, out var userId)
            )
            {
                OrgId? activeOrg = Guid.TryParse(
                    user.FindFirst(LocIntelClaims.ActiveOrg)?.Value,
                    out var orgGuid
                )
                    ? new OrgId(orgGuid)
                    : null;
                // impersonation (ADR 42): live only while unexpired - a pure
                // clock comparison, honoring "never the database" here
                DateTimeOffset? impersonation = null;
                if (
                    long.TryParse(
                        user.FindFirst(LocIntelClaims.ImpersonationExpires)?.Value,
                        out var expiresUnix
                    )
                    && DateTimeOffset.FromUnixTimeSeconds(expiresUnix) is var expires
                    && expires > DateTimeOffset.UtcNow
                )
                    impersonation = expires;
                return new Principal.User(
                    userId,
                    user.FindFirst(LocIntelClaims.Email)?.Value ?? "",
                    user.FindFirst(LocIntelClaims.DisplayName)?.Value,
                    activeOrg,
                    impersonation
                );
            }

            return new Principal.Guest(
                http.Items.TryGetValue(GuestOrgItem, out var v) && v is OrgId org ? org : null
            );
        }
    }
}

/// <summary>
/// ITenantContext backed by the resolved principal (ADR 5/7). In Wolverine
/// message scopes there is no request principal - the tenant is read LAZILY
/// off the scoped IMessageContext's envelope (ADR 24). Lazy is load-bearing,
/// third time now: transactional frames open the database connection before
/// any middleware or handler body runs, so anything the RLS interceptor needs
/// must be answerable at connection-open from whatever scope asks.
/// </summary>
public sealed class PrincipalTenantContext(
    IPrincipalAccessor accessor,
    TenantContext holder,
    Wolverine.IMessageContext messageContext
) : ITenantContext
{
    public OrgId? OrgId =>
        // an explicitly-set holder wins: TenantScope.RunAs is a deliberate
        // elevation (operator acting on a target org, pre-tenant bootstrap)
        holder.OrgId
        ?? accessor.Current switch
        {
            Principal.User u => u.ActiveOrg,
            Principal.Contact c => c.Org,
            Principal.Service s => s.Org,
            Principal.Guest g => g.Org ?? EnvelopeOrg,
            _ => EnvelopeOrg,
        };

    private OrgId? EnvelopeOrg =>
        messageContext.Envelope?.TenantId is { } tenantId
        && Guid.TryParse(tenantId, out var orgGuid)
            ? new OrgId(orgGuid)
            : null;

    public RegionId Region => RegionId.Default; // org->region routing lands with ADR 35 step two
}
