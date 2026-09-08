using LocIntel.Contracts;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Reporting;

/// <summary>Strict PDF admission and its plan usage probe share the reporting transaction.</summary>
public sealed class ReportQuota(
    ReportingDbContext db,
    IEntitlements entitlements,
    TimeProvider time
) : IEntitlementUsageProbe
{
    public string Code => EntitlementCatalog.ReportsMonthly;
    public DateOnly PeriodMonth
    {
        get
        {
            var now = time.GetUtcNow();
            return new DateOnly(now.Year, now.Month, 1);
        }
    }

    public async ValueTask<long> CurrentUsageAsync(OrgId org, CancellationToken ct = default) =>
        await db.QuotaEntries.LongCountAsync(
            x => x.OrgId == org && x.PeriodMonth == PeriodMonth,
            ct
        );

    public sealed record Status(
        bool Enabled,
        long Limit,
        long Consumed,
        long Reserved,
        long Remaining,
        DateOnly PeriodMonth
    );

    public async Task<Status> StatusAsync(OrgId org, CancellationToken ct)
    {
        var month = PeriodMonth;
        var counts = await db
            .QuotaEntries.Where(x => x.OrgId == org && x.PeriodMonth == month)
            .GroupBy(x => x.Consumed)
            .Select(g => new { Consumed = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);
        var consumed = counts.Where(x => x.Consumed).Sum(x => x.Count);
        var reserved = counts.Where(x => !x.Consumed).Sum(x => x.Count);
        var limit = await entitlements.LimitAsync(org, Code, ct);
        return new(
            await entitlements.HasAsync(org, EntitlementCatalog.ReportsEnabled, ct),
            limit,
            consumed,
            reserved,
            Math.Max(0, limit - consumed - reserved),
            month
        );
    }

    // Caller owns the transaction that persists both these entries and the job/outbox.
    // An empty retry (ZIP rebuild only) uses no additional capacity.
    public async Task<EntitlementDecision> ReserveAsync(
        OrgId org,
        Guid jobId,
        Guid[] items,
        CancellationToken ct
    )
    {
        if (!await CapacityReservations.TryLockAsync(db, org, Code, ct))
            throw new CapacityBusyException();
        var month = PeriodMonth;
        var used = await db.QuotaEntries.LongCountAsync(
            x => x.OrgId == org && x.PeriodMonth == month,
            ct
        );
        if (items.Length == 0)
            return new EntitlementDecision(
                EntitlementOutcome.Allowed,
                Code,
                await entitlements.LimitAsync(org, Code, ct),
                used
            );
        var decision = await entitlements.CheckLimitAsync(org, Code, used, items.Length, ct);
        if (!decision.IsAllowed)
            return decision;
        db.QuotaEntries.AddRange(
            items.Select(id => new ReportQuotaEntry
            {
                Id = id,
                OrgId = org,
                JobId = jobId,
                PeriodMonth = month,
            })
        );
        return decision;
    }

    public static Task ReleaseAsync(
        ReportingDbContext db,
        OrgId org,
        Guid jobId,
        CancellationToken ct
    ) =>
        db
            .QuotaEntries.Where(x => x.OrgId == org && x.JobId == jobId && !x.Consumed)
            .ExecuteDeleteAsync(ct);

    public static Task SettleAsync(
        ReportingDbContext db,
        OrgId org,
        Guid itemId,
        bool succeeded,
        CancellationToken ct
    ) =>
        succeeded
            ? db
                .QuotaEntries.Where(x => x.OrgId == org && x.Id == itemId)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Consumed, true), ct)
            : db
                .QuotaEntries.Where(x => x.OrgId == org && x.Id == itemId && !x.Consumed)
                .ExecuteDeleteAsync(ct);
}
