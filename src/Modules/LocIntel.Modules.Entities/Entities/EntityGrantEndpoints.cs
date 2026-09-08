using LocIntel.Contracts;
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
/// Explicit need-to-know: share one entity with one user, for a reason,
/// until a date. Ninety days at most - standing access is a role, not a grant.
/// </summary>
public static class EntityGrantEndpoints
{
    public const int MaxDays = 90;

    [Transactional(typeof(EntitiesDbContext))]
    [WolverinePost("/api/entities/{id}/grants")]
    [ProducesResponseType(typeof(EntityGrantCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Grant(
        Guid id,
        GrantEntityAccessRequest request,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        IMessageBus bus,
        TimeProvider time,
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
        var now = time.GetUtcNow();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return ApiErrors.BadRequest("a grant needs a reason");
        if (request.ExpiresAt <= now || request.ExpiresAt > now.AddDays(MaxDays))
            return ApiErrors.BadRequest($"a grant expires between now and {MaxDays} days out");
        var known = await actors.LabelsAsync([request.UserId], ct);
        if (!known.ContainsKey(request.UserId))
            return Results.NotFound();
        var grant = new EntityAccessGrant
        {
            Id = Guid.CreateVersion7(),
            OrgId = entity!.OrgId,
            EntityId = entity.Id,
            UserId = request.UserId,
            GrantedBy = actor!.Value.Id,
            Reason = request.Reason.Trim(),
            ExpiresAt = request.ExpiresAt,
        };
        db.Grants.Add(grant);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor.Value.Org,
            actor.Value.Audit,
            "entity.access_granted",
            new
            {
                EntityId = entity.Id,
                grant.UserId,
                grant.Reason,
                grant.ExpiresAt,
            }
        );
        return Results.Ok(new EntityGrantCreated(grant.Id, grant.ExpiresAt));
    }

    [Transactional(typeof(EntitiesDbContext))]
    [WolverineDelete("/api/entities/{id}/grants/{grantId}")]
    [ProducesResponseType(typeof(EntityChildRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> Revoke(
        Guid id,
        Guid grantId,
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
        var grant = await db.Grants.FirstOrDefaultAsync(
            g => g.Id == grantId && g.EntityId == id,
            ct
        );
        if (grant is null)
            return Results.NotFound();
        db.Grants.Remove(grant);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor!.Value.Org,
            actor!.Value.Audit,
            "entity.access_revoked",
            new { EntityId = entity!.Id, grant.UserId }
        );
        return Results.Ok(new EntityChildRemoved(grant.Id));
    }
}
