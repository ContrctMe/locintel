using LocIntel.Contracts;

namespace LocIntel.Api;

/// <summary>
/// Materializes the guest tenant (ADR 7: a guest is a guest OF an org) from
/// the request host's first label -> org slug, into HttpContext.Items where
/// RequestPrincipalAccessor reads it. Runs after authentication, only for
/// unauthenticated requests. This middleware owns the single DB lookup so the
/// accessor never queries (no recursion through the RLS interceptor: the
/// organizations table is platform-global, readable with no tenant set).
/// </summary>
public sealed class GuestOrgMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOrganizationLookup orgs)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            // Only the forwarded-header middleware may replace Host after validating the peer.
            var host = context.Request.Host.Host;
            var label = host.Split('.')[0];
            if (
                label.Length > 0
                && !string.Equals(label, "www", StringComparison.OrdinalIgnoreCase)
                && await orgs.FindBySlugAsync(label, context.RequestAborted) is { } org
            )
            {
                context.Items[RequestPrincipalAccessor.GuestOrgItem] = org.Id;
            }
        }
        await next(context);
    }
}
