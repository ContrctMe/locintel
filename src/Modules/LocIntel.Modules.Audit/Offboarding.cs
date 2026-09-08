using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Audit;

/// <summary>
/// Audit's slice of the offboarding export: the org's domain event trail. This
/// is the one module with an exporter and NO purge handler - the trail
/// deliberately outlives the org (retention ages it out).
/// </summary>
public sealed class AuditExporter(AuditDbContext db) : IOrgDataExporter
{
    private const int MaxEvents = 10_000;

    public string Section => "audit";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var events = await db
            .DomainEvents.IgnoreQueryFilters()
            .Where(e => e.OrgId == org.Value)
            .OrderByDescending(e => e.OccurredAt)
            .Take(MaxEvents)
            .Select(e => new
            {
                e.EventName,
                e.ActorTier,
                e.ActorId,
                payload = e.Payload,
                e.OccurredAt,
            })
            .ToBoundedExportListAsync(ct);
        return JsonSerializer.Serialize(
            new
            {
                domainEvents = events,
                truncatedAt = events.Count == MaxEvents ? MaxEvents : (int?)null,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
