using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Organizations;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;

namespace LocIntel.Api;

public sealed record OrgStatusCountResponse(string Status, int Count);

public sealed record OperatorOverviewResponse(
    IReadOnlyList<OrgStatusCountResponse> OrgsByStatus,
    int ClosuresPending,
    int Users,
    int DeadLetters
);

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
                    var gate = await Gate.RequireOperatorAsync(accessor, operators, ct);
                    if (gate is not GateOutcome.Allowed)
                        return gate.ToResult();
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
                        new OperatorOverviewResponse(
                            orgsByStatus
                                .Select(item => new OrgStatusCountResponse(item.status, item.count))
                                .ToList(),
                            closuresPending,
                            users,
                            deadLetters
                        )
                    );
                }
            )
            .Produces<OperatorOverviewResponse>();
    }
}
