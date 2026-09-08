using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Checklists.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace LocIntel.Modules.Checklists;

/// <summary>
/// Checklists' slice of the offboarding export. It was missing entirely
/// until the module catalog exposed it: an org's data export silently
/// omitted every template and completion record it had.
/// </summary>
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
            .ToBoundedExportListAsync(ct);
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
            .ToBoundedExportListAsync(ct);
        return JsonSerializer.Serialize(
            new { templates, checks },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}

/// <summary>Envelope-tenanted purge of the org's checklist rows.</summary>
public static class PurgeOrgChecklistsHandler
{
    [Transactional]
    public static async Task Handle(
        PurgeOrgChecklists _,
        ChecklistsDbContext db,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("purge arrived with no tenant on the envelope");
        await db.Checks.IgnoreQueryFilters().Where(c => c.OrgId == org).ExecuteDeleteAsync(ct);
        await db.Templates.IgnoreQueryFilters().Where(t => t.OrgId == org).ExecuteDeleteAsync(ct);
    }
}
