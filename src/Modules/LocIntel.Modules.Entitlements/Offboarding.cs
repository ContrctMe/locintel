using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Entitlements.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace LocIntel.Modules.Entitlements;

/// <summary>Entitlements' slice of the offboarding export: what the org was entitled to, and its exceptions.</summary>
public sealed class EntitlementsExporter(EntitlementsDbContext db) : IOrgDataExporter
{
    public string Section => "entitlements";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var values = await db
            .OrgEntitlements.IgnoreQueryFilters()
            .Where(e => e.OrgId == org)
            .Select(e => new
            {
                e.Code,
                e.Value,
                e.Source,
                e.UpdatedAt,
            })
            .ToBoundedExportListAsync(ct);
        var exceptions = await db
            .Exceptions.IgnoreQueryFilters()
            .Where(e => e.OrgId == org)
            .Select(e => new
            {
                e.Code,
                e.Value,
                e.Reason,
                e.ExpiresAt,
                e.CreatedAt,
            })
            .ToBoundedExportListAsync(ct);
        var rollups = await db
            .Rollups.IgnoreQueryFilters()
            .Where(r => r.OrgId == org)
            .Select(r => new
            {
                r.Code,
                r.PeriodMonth,
                r.Amount,
            })
            .ToBoundedExportListAsync(ct);
        return JsonSerializer.Serialize(
            new
            {
                values,
                exceptions,
                rollups,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}

/// <summary>Envelope-tenanted purge of the org's entitlement state.</summary>
public static class PurgeOrgEntitlementsHandler
{
    [Transactional]
    public static async Task Handle(
        PurgeOrgEntitlements _,
        EntitlementsDbContext db,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("purge arrived with no tenant on the envelope");
        await db.UsageEvents.IgnoreQueryFilters().Where(e => e.OrgId == org).ExecuteDeleteAsync(ct);
        await db.Rollups.IgnoreQueryFilters().Where(r => r.OrgId == org).ExecuteDeleteAsync(ct);
        await db.Exceptions.IgnoreQueryFilters().Where(e => e.OrgId == org).ExecuteDeleteAsync(ct);
        await db
            .Subscriptions.IgnoreQueryFilters()
            .Where(s => s.OrgId == org)
            .ExecuteDeleteAsync(ct);
        await db
            .OrgEntitlements.IgnoreQueryFilters()
            .Where(e => e.OrgId == org)
            .ExecuteDeleteAsync(ct);
    }
}
