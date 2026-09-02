using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Alerts.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Alerts;

/// <summary>Alerts' slice of the offboarding export: bulletins with acknowledgements, and the feed.</summary>
public sealed class AlertsExporter(AlertsDbContext db) : IOrgDataExporter
{
    public string Section => "alerts";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var bulletins = await db
            .Bulletins.IgnoreQueryFilters()
            .Where(b => b.OrgId == org)
            .Select(b => new
            {
                b.Id,
                kind = b.Kind.ToString(),
                severity = b.Severity.ToString(),
                b.Title,
                b.Body,
                scopePath = b.ScopePath == null ? null : b.ScopePath.ToString(),
                b.EntityId,
                b.IncidentId,
                b.CaseId,
                b.IssuedAt,
                b.ExpiresAt,
                status = b.Status.ToString(),
                acknowledgements = db
                    .Acknowledgements.IgnoreQueryFilters()
                    .Where(a => a.BulletinId == b.Id)
                    .Select(a => new
                    {
                        a.UserId,
                        a.SiteId,
                        a.Note,
                        a.AcknowledgedAt,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);
        var alerts = await db
            .Alerts.IgnoreQueryFilters()
            .Where(a => a.OrgId == org)
            .Select(a => new
            {
                a.Id,
                kind = a.Kind.ToString(),
                severity = a.Severity.ToString(),
                a.Title,
                a.IncidentId,
                a.BulletinId,
                a.CreatedAt,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new { bulletins, alerts },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
