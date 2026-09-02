using LocIntel.Contracts;
using LocIntel.Contracts.Entities;
using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Cases.Cases.Api;
using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Intelligence;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>
/// Draft a case brief from what the case holds. Entities go through the
/// need-to-know directory, so a reader without reach sees "restricted"
/// in the draft rather than a name; nothing is stored unless the person
/// files the draft as a note themselves.
/// </summary>
public static class CaseBriefEndpoint
{
    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/assist/brief")]
    [ProducesResponseType(typeof(CaseBriefResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Draft(
        Guid id,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntitlements entitlements,
        IIncidentDirectory incidents,
        IEntityDirectory entities,
        IStoredFileLookup files,
        ITextIntelligence intelligence,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseAccess.LoadAsync(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (!access!.CanWork)
            return Results.Unauthorized();
        if (!await entitlements.HasAsync(access.Actor.Org, EntitlementCatalog.AiAssist, ct))
            return Results.Json(
                new
                {
                    error = "AI assistance is not part of this plan",
                    code = EntitlementCatalog.AiAssist,
                },
                statusCode: StatusCodes.Status402PaymentRequired
            );
        var @case = access.Case;
        var incidentLines = new List<string>();
        foreach (
            var link in await db
                .Incidents.Where(i => i.CaseId == id)
                .OrderBy(i => i.AddedAt)
                .ToListAsync(ct)
        )
        {
            var incident = await incidents.FindAsync(link.IncidentId, ct);
            incidentLines.Add(
                incident is null
                    ? "an incident no longer available"
                    : $"{incident.OccurredAt:yyyy-MM-dd HH:mm} UTC, {incident.Category} ({incident.Severity}, {incident.Status}): {incident.Title}"
            );
        }
        var entityLinks = await db.Entities.Where(e => e.CaseId == id).ToListAsync(ct);
        var visible = await entities.LookupVisibleAsync(
            entityLinks.Select(e => e.EntityId).ToList(),
            ct
        );
        var entityLines = entityLinks
            .Select(l =>
                visible.TryGetValue(l.EntityId, out var e)
                    ? $"{e.Kind} \"{e.DisplayName}\" ({e.Status}){(e.Aliases.Length > 0 ? $", aka {string.Join(", ", e.Aliases)}" : "")}{(l.Note is null ? "" : $" - {l.Note}")}"
                    : "a restricted record (need-to-know)"
            )
            .ToList();
        var notes = await db
            .Notes.Where(n => n.CaseId == id)
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.Body)
            .ToListAsync(ct);
        var evidenceLines = new List<string>();
        foreach (
            var item in await db
                .Evidence.Where(e => e.CaseId == id)
                .OrderBy(e => e.AddedAt)
                .ToListAsync(ct)
        )
        {
            var file = await files.GetAsync(item.FileId, ct);
            evidenceLines.Add(
                $"{file?.Name ?? "file"}{(item.Label is null ? "" : $" - {item.Label}")} (added {item.AddedAt:yyyy-MM-dd})"
            );
        }
        var text = await intelligence.DraftCaseBriefAsync(
            new CaseBriefInput(
                @case.Title,
                @case.Summary,
                incidentLines,
                entityLines,
                notes,
                evidenceLines
            ),
            ct
        );
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            "case.brief_drafted",
            new { @case.Id, intelligence.Provider }
        );
        return Results.Ok(new CaseBriefResponse(intelligence.Provider, text));
    }
}
