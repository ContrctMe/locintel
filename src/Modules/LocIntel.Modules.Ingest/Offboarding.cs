using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace LocIntel.Modules.Ingest;

/// <summary>
/// Ingest's slice of the offboarding export: connector configuration (never
/// the encrypted credentials) and batch history.
/// </summary>
public sealed class IngestExporter(IngestDbContext db) : IOrgDataExporter
{
    public string Section => "ingest";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var connectors = await db
            .Connectors.IgnoreQueryFilters()
            .Where(c => c.OrgId == org)
            .Select(c => new
            {
                c.Name,
                c.Type,
                c.Url,
                c.CreatedAt,
            })
            .ToListAsync(ct);
        var batches = await db
            .Batches.IgnoreQueryFilters()
            .Where(b => b.OrgId == org)
            .Select(b => new
            {
                b.Source,
                status = b.Status.ToString(),
                b.CreatedAt,
                counts = b.Counts,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new { connectors, batches },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}

/// <summary>Envelope-tenanted purge of the org's ingest state, encrypted connector credentials included.</summary>
public static class PurgeOrgIngestHandler
{
    [Transactional]
    public static async Task Handle(
        PurgeOrgIngest _,
        IngestDbContext db,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("purge arrived with no tenant on the envelope");
        await db.StagedSites.IgnoreQueryFilters().Where(s => s.OrgId == org).ExecuteDeleteAsync(ct);
        await db.Batches.IgnoreQueryFilters().Where(b => b.OrgId == org).ExecuteDeleteAsync(ct);
        await db.Connectors.IgnoreQueryFilters().Where(c => c.OrgId == org).ExecuteDeleteAsync(ct);
    }
}
