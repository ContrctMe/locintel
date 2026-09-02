using Microsoft.EntityFrameworkCore;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Api;

/// <summary>
/// Suspension enforcement (org lifecycle back half): a Suspended org's
/// members can still sign in, switch orgs, and see /me - but every /api/*
/// call answers 403 org_suspended. Reads the org_directory read model (one
/// indexed PK lookup), which learns of transitions via OrganizationUpserted.
/// </summary>
public sealed class SuspensionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IPrincipalAccessor accessor,
        IdentityDbContext db
    )
    {
        OrgId? principalOrg = accessor.Current switch
        {
            Principal.User { ActiveOrg: { } userOrg } => userOrg,
            Principal.Service service => service.Org, // keys of a dead org stop too
            _ => null,
        };
        if (context.Request.Path.StartsWithSegments("/api") && principalOrg is { } org)
        {
            var status = await db
                .OrgDirectory.Where(d => d.OrgId == org)
                .Select(d => d.Status)
                .FirstOrDefaultAsync(context.RequestAborted);
            if (status is "Suspended" or "Offboarding")
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    status == "Suspended"
                        ? new { error = "organization suspended", code = "org_suspended" }
                        : new { error = "organization offboarded", code = "org_offboarded" }
                );
                return;
            }
        }
        await next(context);
    }
}
