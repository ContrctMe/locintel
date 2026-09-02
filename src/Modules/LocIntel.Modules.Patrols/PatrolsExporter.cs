using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Patrols.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Patrols;

/// <summary>Patrols' slice of the offboarding export: routes, schedules, and every run with its scans.</summary>
public sealed class PatrolsExporter(PatrolsDbContext db) : IOrgDataExporter
{
    public string Section => "patrols";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var routes = await db
            .Routes.IgnoreQueryFilters()
            .Where(r => r.OrgId == org)
            .Select(r => new
            {
                r.Id,
                r.SiteId,
                r.Name,
                checkpoints = r.CheckpointsJson,
                r.ExpectedMinutes,
                r.Archived,
                schedules = db
                    .Schedules.IgnoreQueryFilters()
                    .Where(s => s.RouteId == r.Id)
                    .Select(s => new
                    {
                        s.RRule,
                        s.AnchorDate,
                        s.StartLocal,
                        s.ExDates,
                        s.Active,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);
        var patrols = await db
            .Patrols.IgnoreQueryFilters()
            .Where(p => p.OrgId == org)
            .Select(p => new
            {
                p.Id,
                p.RouteId,
                p.SiteId,
                p.BusinessDate,
                p.ScheduledStartLocal,
                status = p.Status.ToString(),
                p.StartedAt,
                p.StartedBy,
                p.EndedAt,
                p.Summary,
                scans = db
                    .Scans.IgnoreQueryFilters()
                    .Where(s => s.PatrolId == p.Id)
                    .OrderBy(s => s.ScannedAt)
                    .Select(s => new
                    {
                        s.Code,
                        s.ScannedAt,
                        s.Latitude,
                        s.Longitude,
                        s.DistanceMeters,
                        s.Note,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new { routes, patrols },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
