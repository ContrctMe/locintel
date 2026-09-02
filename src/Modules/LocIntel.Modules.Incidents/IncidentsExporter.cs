using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Incidents.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Incidents;

/// <summary>Incidents' slice of the offboarding export, trash included (it is the org's data).</summary>
public sealed class IncidentsExporter(IncidentsDbContext db) : IOrgDataExporter
{
    public string Section => "incidents";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var incidents = await db
            .Incidents.IgnoreQueryFilters()
            .Where(i => i.OrgId == org)
            .OrderBy(i => i.OccurredAt)
            .Select(i => new
            {
                i.Id,
                i.SiteId,
                path = i.Path.ToString(),
                category = i.Category.ToString(),
                severity = i.Severity.ToString(),
                status = i.Status.ToString(),
                source = i.Source.ToString(),
                i.Title,
                i.Narrative,
                i.LocationDetail,
                i.OccurredAt,
                i.BusinessDate,
                i.ReportedAt,
                i.LossAmount,
                i.RecoveredAmount,
                i.Currency,
                i.PoliceReportNumber,
                i.Tags,
                i.ClosedAt,
                i.ClosureReason,
                i.LegalHold,
                i.DeletedAt,
                notes = db
                    .Notes.IgnoreQueryFilters()
                    .Where(n => n.IncidentId == i.Id)
                    .Select(n => new
                    {
                        n.AuthorId,
                        n.Body,
                        n.CreatedAt,
                        n.DeletedAt,
                    })
                    .ToList(),
                attachments = db
                    .Attachments.IgnoreQueryFilters()
                    .Where(a => a.IncidentId == i.Id)
                    .Select(a => new
                    {
                        a.FileId,
                        a.Label,
                        a.AddedAt,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new { incidents },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
