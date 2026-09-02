using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Cases;

/// <summary>Cases' slice of the offboarding export, custody chain included.</summary>
public sealed class CasesExporter(CasesDbContext db) : IOrgDataExporter
{
    public string Section => "cases";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var cases = await db
            .Cases.IgnoreQueryFilters()
            .Where(c => c.OrgId == org)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.Summary,
                status = c.Status.ToString(),
                priority = c.Priority.ToString(),
                c.LeadId,
                disposition = c.Disposition.ToString(),
                c.ClosedAt,
                c.ClosureNote,
                c.LegalHold,
                c.CreatedAt,
                c.DeletedAt,
                incidents = db
                    .Incidents.IgnoreQueryFilters()
                    .Where(i => i.CaseId == c.Id)
                    .Select(i => i.IncidentId)
                    .ToList(),
                entities = db
                    .Entities.IgnoreQueryFilters()
                    .Where(e => e.CaseId == c.Id)
                    .Select(e => new { e.EntityId, e.Note })
                    .ToList(),
                members = db
                    .Members.IgnoreQueryFilters()
                    .Where(m => m.CaseId == c.Id)
                    .Select(m => new { m.UserId, role = m.Role.ToString() })
                    .ToList(),
                tasks = db
                    .Tasks.IgnoreQueryFilters()
                    .Where(t => t.CaseId == c.Id)
                    .Select(t => new
                    {
                        t.Title,
                        t.AssigneeId,
                        t.DueAt,
                        t.DoneAt,
                        t.DeletedAt,
                    })
                    .ToList(),
                notes = db
                    .Notes.IgnoreQueryFilters()
                    .Where(n => n.CaseId == c.Id)
                    .Select(n => new
                    {
                        n.AuthorId,
                        n.Body,
                        n.CreatedAt,
                        n.DeletedAt,
                    })
                    .ToList(),
                evidence = db
                    .Evidence.IgnoreQueryFilters()
                    .Where(e => e.CaseId == c.Id)
                    .Select(e => new
                    {
                        e.FileId,
                        e.Label,
                        e.AddedAt,
                    })
                    .ToList(),
                custody = db
                    .Custody.IgnoreQueryFilters()
                    .Where(x => x.CaseId == c.Id)
                    .OrderBy(x => x.At)
                    .Select(x => new
                    {
                        x.FileId,
                        action = x.Action.ToString(),
                        x.ActorId,
                        x.ActorTier,
                        x.Detail,
                        x.At,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new { cases },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
