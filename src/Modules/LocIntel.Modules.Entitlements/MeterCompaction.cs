using LocIntel.Contracts;
using LocIntel.Modules.Entitlements.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace LocIntel.Modules.Entitlements;

/// <summary>Per-org meter compaction (append-then-rollup consequence): fold events older than an hour into the monthly rollup.</summary>
public sealed record CompactMeters;

public static class CompactMetersHandler
{
    public static async Task Handle(
        CompactMeters _,
        Envelope envelope,
        ITenantContext tenant,
        EntitlementsDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"CompactMeters arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var cutoff = time.GetUtcNow().AddHours(-1);
        var groups = await db
            .UsageEvents.Where(e => e.OccurredAt < cutoff)
            .GroupBy(e => new
            {
                e.Code,
                e.OccurredAt.Year,
                e.OccurredAt.Month,
            })
            .Select(g => new
            {
                g.Key.Code,
                g.Key.Year,
                g.Key.Month,
                Total = g.Sum(e => e.Amount),
            })
            .ToListAsync(ct);

        foreach (var group in groups)
        {
            var month = new DateOnly(group.Year, group.Month, 1);
            var rollup = await db.Rollups.FirstOrDefaultAsync(
                r => r.Code == group.Code && r.PeriodMonth == month,
                ct
            );
            if (rollup is null)
            {
                db.Rollups.Add(
                    new MeterRollup
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = org,
                        Code = group.Code,
                        PeriodMonth = month,
                        Amount = group.Total,
                        CompactedThrough = cutoff,
                    }
                );
            }
            else
            {
                rollup.Amount += group.Total;
                rollup.CompactedThrough = cutoff;
            }
        }
        await db.UsageEvents.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Daily enumerator fanning out per-org compaction (ADR 24 pattern).</summary>
public sealed class MeterCompactionService(IServiceProvider services)
    : PerOrgSweepService<CompactMeters>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);
}
