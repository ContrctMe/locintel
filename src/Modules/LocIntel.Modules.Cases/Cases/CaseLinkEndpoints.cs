using LocIntel.Contracts.Entities;
using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Cases.Cases.Api;
using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>Incidents and entities on a case (cases:manage). Both resolve through contracts: tenant-filtered, and entities need-to-know filtered.</summary>
public static class CaseLinkEndpoints
{
    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/incidents")]
    [ProducesResponseType(typeof(CaseChildAdded), StatusCodes.Status200OK)]
    public static async Task<IResult> AddIncident(
        Guid id,
        AddCaseIncidentRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IIncidentDirectory incidents,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.CasesManage, ct);
        var incident = await incidents.FindAsync(request.IncidentId, ct);
        if (incident is null || !scope.Covers(incident.Path))
            return Results.NotFound();
        if (await db.Incidents.AnyAsync(i => i.CaseId == id && i.IncidentId == incident.Id, ct))
            return Results.Conflict(new { error = "incident is already on the case" });
        var link = CaseEndpoints.NewLink(access!.Case, incident, access.Actor);
        db.Incidents.Add(link);
        access.Case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await CaseAudit.PublishAsync(
            bus,
            access.Actor,
            "case.incident_added",
            new { CaseId = id, IncidentId = incident.Id }
        );
        return Results.Ok(new CaseChildAdded(link.Id));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverineDelete("/api/cases/{id}/incidents/{linkId}")]
    [ProducesResponseType(typeof(CaseChildRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> RemoveIncident(
        Guid id,
        Guid linkId,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var link = await db.Incidents.FirstOrDefaultAsync(
            i => i.Id == linkId && i.CaseId == id,
            ct
        );
        if (link is null)
            return Results.NotFound();
        db.Incidents.Remove(link);
        access!.Case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await CaseAudit.PublishAsync(
            bus,
            access.Actor,
            "case.incident_removed",
            new { CaseId = id, link.IncidentId }
        );
        return Results.Ok(new CaseChildRemoved(link.Id));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/entities")]
    [ProducesResponseType(typeof(CaseChildAdded), StatusCodes.Status200OK)]
    public static async Task<IResult> AddEntity(
        Guid id,
        AddCaseEntityRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntityDirectory entities,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        // you can only put on a case what you may see yourself
        var visible = await entities.LookupVisibleAsync([request.EntityId], ct);
        if (!visible.ContainsKey(request.EntityId))
            return Results.NotFound();
        if (await db.Entities.AnyAsync(e => e.CaseId == id && e.EntityId == request.EntityId, ct))
            return Results.Conflict(new { error = "entity is already on the case" });
        var link = new CaseEntity
        {
            Id = Guid.CreateVersion7(),
            OrgId = access!.Case.OrgId,
            CaseId = id,
            EntityId = request.EntityId,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            AddedBy = access.Actor.Id,
        };
        db.Entities.Add(link);
        access.Case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await CaseAudit.PublishAsync(
            bus,
            access.Actor,
            "case.entity_added",
            new { CaseId = id, request.EntityId }
        );
        return Results.Ok(new CaseChildAdded(link.Id));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverineDelete("/api/cases/{id}/entities/{linkId}")]
    [ProducesResponseType(typeof(CaseChildRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> RemoveEntity(
        Guid id,
        Guid linkId,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var link = await db.Entities.FirstOrDefaultAsync(e => e.Id == linkId && e.CaseId == id, ct);
        if (link is null)
            return Results.NotFound();
        db.Entities.Remove(link);
        access!.Case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await CaseAudit.PublishAsync(
            bus,
            access.Actor,
            "case.entity_removed",
            new { CaseId = id, link.EntityId }
        );
        return Results.Ok(new CaseChildRemoved(link.Id));
    }
}
