using LocIntel.Modules.Entitlements.Data;
using LocIntel.Platform.Billing;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Entitlements;

/// <summary>
/// The drained migrate role fills newly introduced plan codes. Existing assignments
/// and exceptions are never rewritten; billing remains the subscription writer.
/// </summary>
public static class PlanEntitlementBackfill
{
    public static async Task<int> RunAsync(EntitlementsDbContext db, CancellationToken ct = default)
    {
        var inserted = 0;
        Guid? after = null;
        while (true)
        {
            var batch = await db
                .Subscriptions.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => after == null || s.Id.CompareTo(after.Value) > 0)
                .OrderBy(s => s.Id)
                .Take(200)
                .ToListAsync(ct);
            if (batch.Count == 0)
                return inserted;
            foreach (var subscription in batch)
            {
                if (
                    subscription.Status
                        is not (
                            SubscriptionStatus.Active
                            or SubscriptionStatus.Trialing
                            or SubscriptionStatus.PastDue
                        )
                    || PlanCatalog.Find(subscription.PlanId) is not { } plan
                )
                    continue;
                foreach (var (code, value) in plan.Entitlements)
                    inserted += await db.Database.ExecuteSqlAsync(
                        $"""
                        INSERT INTO entitlements.org_entitlements (id, org_id, code, value, source, updated_at)
                        SELECT {Guid.CreateVersion7()}, org_id, {code}, {value}, {$"plan:{plan.Id}"}, now()
                        FROM entitlements.org_subscriptions
                        WHERE id = {subscription.Id} AND plan_id = {plan.Id}
                          AND status IN ({SubscriptionStatus.Active}, {SubscriptionStatus.Trialing}, {SubscriptionStatus.PastDue})
                        ON CONFLICT (org_id, code) DO NOTHING
                        """,
                        ct
                    );
            }
            after = batch[^1].Id;
        }
    }
}
