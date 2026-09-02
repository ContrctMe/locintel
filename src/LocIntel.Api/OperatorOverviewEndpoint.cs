using Microsoft.EntityFrameworkCore;
using LocIntel.Modules.Identity.Data;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Organizations;
using LocIntel.Platform.Kernel;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;

namespace LocIntel.Api;

/// <summary>
/// The platform-at-a-glance numbers (operability item 10): org counts by
/// status, pending self-serve closures, people, and dead letters. Reads only
/// platform-global tables plus the message store - per-org detail (webhook
/// deliveries, usage) stays on the per-org surfaces where RLS applies.
/// </summary>
public static class OperatorOverviewEndpoint
{
    public static void MapOperatorOverviewEndpoint(this WebApplication app)
    {
        app.MapGet(
            "/api/operator/overview",
            async (
                IPrincipalAccessor accessor,
                IOperatorContext operators,
                TenancyDbContext tenancy,
                IdentityDbContext identity,
                IMessageStore store,
                CancellationToken ct
            ) =>
            {
                if (!await operators.IsOperatorAsync(accessor.Current, ct))
                    return Results.Unauthorized();
                var orgsByStatus = await tenancy
                    .Organizations.Where(o => !o.IsPlatform)
                    .GroupBy(o => o.Status)
                    .Select(g => new { status = g.Key.ToString(), count = g.Count() })
                    .ToListAsync(ct);
                var closuresPending = await tenancy.Organizations.CountAsync(
                    o => o.CloseRequestedAt != null && o.Status == OrganizationStatus.Active,
                    ct
                );
                var users = await identity.Users.CountAsync(ct);
                var deadLetters = (
                    await store.DeadLetters.QueryAsync(
                        new DeadLetterEnvelopeQuery { PageSize = 1 },
                        ct
                    )
                ).TotalCount;
                return Results.Ok(
                    new
                    {
                        orgsByStatus,
                        closuresPending,
                        users,
                        deadLetters,
                    }
                );
            }
        );
    }
}
