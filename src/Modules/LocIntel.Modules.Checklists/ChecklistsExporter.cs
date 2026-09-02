using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Checklists.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Checklists;

/// <summary>Checklists' slice of the offboarding export: templates and the completion trail.</summary>
public sealed class ChecklistsExporter(ChecklistsDbContext db) : IOrgDataExporter
{
    public string Section => "checklists";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var templates = await db
            .Templates.IgnoreQueryFilters()
            .Where(t => t.OrgId == org)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Items,
                t.ScopePath,
                t.CreatedAt,
            })
            .ToListAsync(ct);
        var checks = await db
            .Checks.IgnoreQueryFilters()
            .Where(c => c.OrgId == org)
            .Select(c => new
            {
                c.TemplateId,
                c.SiteId,
                c.BusinessDate,
                c.ItemIndex,
                c.CheckedAt,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new { templates, checks },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
