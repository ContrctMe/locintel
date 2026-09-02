using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Entities.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Entities;

/// <summary>
/// Entities' slice of the offboarding export. These are records about
/// real people: the export is the org's, and it carries the retention
/// dates and holds so the receiving system inherits the obligations.
/// </summary>
public sealed class EntitiesExporter(EntitiesDbContext db) : IOrgDataExporter
{
    public string Section => "entities";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var entities = await db
            .Entities.IgnoreQueryFilters()
            .Where(e => e.OrgId == org)
            .Select(e => new
            {
                e.Id,
                kind = e.Kind.ToString(),
                status = e.Status.ToString(),
                e.DisplayName,
                e.Aliases,
                descriptors = e.DescriptorsJson,
                e.Summary,
                e.ExpiresAt,
                e.LegalHold,
                e.CreatedAt,
                e.DeletedAt,
                incidents = db
                    .Links.IgnoreQueryFilters()
                    .Where(l => l.EntityId == e.Id)
                    .Select(l => new
                    {
                        l.IncidentId,
                        role = l.Role.ToString(),
                        l.Note,
                        l.LinkedAt,
                    })
                    .ToList(),
                grants = db
                    .Grants.IgnoreQueryFilters()
                    .Where(g => g.EntityId == e.Id)
                    .Select(g => new
                    {
                        g.UserId,
                        g.Reason,
                        g.ExpiresAt,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new { entities },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
