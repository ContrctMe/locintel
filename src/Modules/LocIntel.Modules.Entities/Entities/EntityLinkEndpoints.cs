using LocIntel.Contracts.Entities;
using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Entities.Data;
using LocIntel.Modules.Entities.Entities.Api;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Entities.Entities;

/// <summary>
/// The graph's edges. Linking needs entities:manage over the incident's
/// site (gate 3 on the write); the incident resolves through the Incidents
/// contract, tenant-filtered, so another org's incident id is a 404.
/// </summary>
public static class EntityLinkEndpoints
{
    [Transactional(typeof(EntitiesDbContext))]
    [WolverinePost("/api/entities/{id}/links")]
    [ProducesResponseType(typeof(EntityLinkCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Link(
        Guid id,
        LinkIncidentRequest request,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IIncidentDirectory incidents,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (entity, actor, error) = await EntityEndpoints.LoadForManage(
            id,
            db,
            accessor,
            scopes,
            ct
        );
        if (error is not null)
            return error;
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.EntitiesManage, ct);
        var incident = await incidents.FindAsync(request.IncidentId, ct);
        if (incident is null || !scope.Covers(incident.Path))
            return Results.NotFound();
        if (await db.Links.AnyAsync(l => l.EntityId == id && l.IncidentId == incident.Id, ct))
            return Results.Conflict(new { error = "already linked to that incident" });
        var link = new EntityIncidentLink
        {
            Id = Guid.CreateVersion7(),
            OrgId = entity!.OrgId,
            EntityId = entity.Id,
            IncidentId = incident.Id,
            SiteId = incident.SiteId,
            Path = new LTree(incident.Path),
            Role = request.Role,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            LinkedBy = actor!.Value.Id,
        };
        db.Links.Add(link);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await EntityAudit.PublishAsync(
            bus,
            actor.Value,
            "entity.linked",
            new
            {
                EntityId = entity.Id,
                incident.Id,
                Role = link.Role.ToString(),
            }
        );
        var linkCount = await db.Links.CountAsync(l => l.EntityId == entity.Id, ct);
        await bus.PublishAsync(
            new EntityLinked(
                entity.Id,
                entity.Kind.ToString(),
                entity.DisplayName,
                incident.Id,
                incident.Title,
                incident.SiteId,
                incident.Path,
                linkCount,
                actor.Value.Id
            ),
            new DeliveryOptions { TenantId = entity.OrgId.Value.ToString() }
        );
        return Results.Ok(new EntityLinkCreated(link.Id));
    }

    [Transactional(typeof(EntitiesDbContext))]
    [WolverineDelete("/api/entities/{id}/links/{linkId}")]
    [ProducesResponseType(typeof(EntityChildRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> Unlink(
        Guid id,
        Guid linkId,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (entity, actor, error) = await EntityEndpoints.LoadForManage(
            id,
            db,
            accessor,
            scopes,
            ct
        );
        if (error is not null)
            return error;
        var link = await db.Links.FirstOrDefaultAsync(l => l.Id == linkId && l.EntityId == id, ct);
        if (link is null)
            return Results.NotFound();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.EntitiesManage, ct);
        if (!scope.Covers(link.Path.ToString()))
            return Results.NotFound();
        db.Links.Remove(link); // tier 3
        entity!.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await EntityAudit.PublishAsync(
            bus,
            actor!.Value,
            "entity.unlinked",
            new { EntityId = entity.Id, link.IncidentId }
        );
        return Results.Ok(new EntityChildRemoved(link.Id));
    }
}
