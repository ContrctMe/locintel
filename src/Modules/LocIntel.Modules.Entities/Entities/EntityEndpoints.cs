using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Entities.Data;
using LocIntel.Modules.Entities.Entities.Api;
using LocIntel.Modules.Entities.Retention;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Entities.Entities;

/// <summary>
/// Persons of interest, vehicles, groups. Reads pass four gates - the fourth
/// (EntityVisibility) is need-to-know and filters like scope does. Every
/// detail read is a domain audit event ("entity.viewed") regardless of the
/// org's access-log policy: a person record's readers are always on record.
/// </summary>
public static class EntityEndpoints
{
    [NonTransactional]
    [WolverineGet("/api/entities")]
    [ProducesResponseType(typeof(EntityListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        EntityKind? kind,
        EntityStatus? status,
        Guid? incidentId,
        string? q,
        bool? trash,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.EntitiesRead, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var manage = await scopes.CanAsync(accessor.Current, Capabilities.EntitiesManage, ct);
        if (trash is true && !manage)
            return new GateOutcome.Forbidden(Capabilities.EntitiesManage).ToResult();
        var now = time.GetUtcNow();
        var query = EntityVisibility.Visible(db, scope, accessor.Current, manage, now);
        if (trash is true)
            query = query
                .IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter])
                .Where(e => e.DeletedAt != null);
        if (kind is { } k)
            query = query.Where(e => e.Kind == k);
        if (status is { } s)
            query = query.Where(e => e.Status == s);
        if (incidentId is { } incident)
            query = query.Where(e =>
                db.Links.Any(l => l.EntityId == e.Id && l.IncidentId == incident)
            );
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = $"%{q.Trim()}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.DisplayName, needle)
                || e.Aliases.Any(a => EF.Functions.ILike(a, needle))
            );
        }
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var items = await query
            .OrderBy(e => e.DisplayName)
            .ThenBy(e => e.Id)
            .Skip(skip)
            .Take(take)
            .Select(e => new EntitySummary(
                e.Id,
                e.Kind,
                e.Status,
                e.DisplayName,
                e.Aliases,
                db.Links.Count(l => l.EntityId == e.Id),
                e.ExpiresAt,
                e.LegalHold,
                e.DeletedAt
            ))
            .ToListAsync(ct);
        return Results.Ok(
            new EntityListResponse(
                items,
                total,
                skip + items.Count < total ? skip + items.Count : null
            )
        );
    }

    [Transactional(
        typeof(EntitiesDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/entities/{id}")]
    [ProducesResponseType(typeof(EntityDetail), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        IIncidentDirectory incidents,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.EntitiesRead, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var manage = await scopes.CanAsync(accessor.Current, Capabilities.EntitiesManage, ct);
        var now = time.GetUtcNow();
        var visible = EntityVisibility.Visible(db, scope, accessor.Current, manage, now);
        if (manage)
            visible = visible.IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter]);
        var entity = await visible.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
            return Results.NotFound();

        // the reader sees only the edges they could see the incident of
        var links = await (manage ? db.Links : db.Links.InScope(scope))
            .Where(l => l.EntityId == id)
            .OrderByDescending(l => l.LinkedAt)
            .ToListAsync(ct);
        var linkViews = new List<EntityLinkView>(links.Count);
        foreach (var link in links)
        {
            var incident = await incidents.FindAsync(link.IncidentId, ct);
            linkViews.Add(
                new EntityLinkView(
                    link.Id,
                    link.IncidentId,
                    link.SiteId,
                    incident?.Title,
                    incident?.Status,
                    incident?.OccurredAt,
                    link.Role,
                    link.Note,
                    link.LinkedAt
                )
            );
        }
        var grants = manage
            ? await db.Grants.Where(g => g.EntityId == id).OrderBy(g => g.ExpiresAt).ToListAsync(ct)
            : null;
        var labels = await actors.LabelsAsync(
            (grants ?? []).Select(g => g.UserId).Append(entity.CreatedBy).Distinct().ToList(),
            ct
        );

        // the fifth audit kind (blueprint): who looked at a person record
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            "entity.viewed",
            new { entity.Id, Kind = entity.Kind.ToString() }
        );
        return Results.Ok(
            new EntityDetail(
                entity.Id,
                entity.Kind,
                entity.Status,
                entity.DisplayName,
                entity.Aliases,
                Descriptors(entity.DescriptorsJson),
                entity.Summary,
                entity.ExpiresAt,
                entity.LegalHold,
                entity.CreatedBy,
                labels.GetValueOrDefault(entity.CreatedBy),
                entity.CreatedAt,
                entity.UpdatedAt,
                entity.DeletedAt,
                linkViews,
                grants
                    ?.Select(g => new EntityGrantView(
                        g.Id,
                        g.UserId,
                        labels.GetValueOrDefault(g.UserId),
                        g.Reason,
                        g.ExpiresAt,
                        g.CreatedAt
                    ))
                    .ToList()
            )
        );
    }

    [Transactional(typeof(EntitiesDbContext))]
    [WolverinePost("/api/entities")]
    [ProducesResponseType(typeof(EntityCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateEntityRequest request,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.EntitiesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.EntitiesManage).ToResult();
        var now = time.GetUtcNow();
        var expiresAt = request.ExpiresAt ?? EntityRetention.Default(now);
        if (Validate(request.DisplayName, request.Descriptors, expiresAt, now) is { } error)
            return Results.BadRequest(new { error });
        var entity = new Entity
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            Kind = request.Kind,
            DisplayName = request.DisplayName.Trim(),
            Aliases = CleanAliases(request.Aliases),
            DescriptorsJson = JsonSerializer.Serialize(CleanDescriptors(request.Descriptors)),
            Summary = request.Summary?.Trim() ?? "",
            ExpiresAt = expiresAt,
            CreatedBy = actor.Id,
        };
        db.Entities.Add(entity);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            "entity.created",
            new
            {
                entity.Id,
                Kind = entity.Kind.ToString(),
                entity.ExpiresAt,
            }
        );
        return Results.Ok(new EntityCreated(entity.Id, entity.ExpiresAt));
    }

    [Transactional(typeof(EntitiesDbContext))]
    [WolverinePut("/api/entities/{id}")]
    [ProducesResponseType(typeof(EntityMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Update(
        Guid id,
        UpdateEntityRequest request,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var (entity, actor, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var now = time.GetUtcNow();
        if (
            Validate(request.DisplayName, request.Descriptors, request.ExpiresAt, now) is
            { } invalid
        )
            return ApiErrors.BadRequest(invalid);
        entity!.DisplayName = request.DisplayName.Trim();
        entity.Aliases = CleanAliases(request.Aliases);
        entity.DescriptorsJson = JsonSerializer.Serialize(CleanDescriptors(request.Descriptors));
        entity.Summary = request.Summary?.Trim() ?? "";
        entity.ExpiresAt = request.ExpiresAt;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor!.Value.Org,
            actor!.Value.Audit,
            "entity.updated",
            new { entity.Id }
        );
        return Results.Ok(Mutated(entity));
    }

    /// <summary>Confirmed needs evidence: at least one linked incident.</summary>
    [Transactional(typeof(EntitiesDbContext))]
    [WolverinePost("/api/entities/{id}/status")]
    [ProducesResponseType(typeof(EntityMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> SetStatus(
        Guid id,
        SetEntityStatusRequest request,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (entity, actor, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (
            request.Status == EntityStatus.Confirmed
            && !await db.Links.AnyAsync(l => l.EntityId == id, ct)
        )
            return ApiErrors.Conflict(
                "confirming requires at least one linked incident as evidence"
            );
        entity!.Status = request.Status;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor!.Value.Org,
            actor!.Value.Audit,
            "entity.status_changed",
            new { entity.Id, Status = entity.Status.ToString() }
        );
        return Results.Ok(Mutated(entity));
    }

    [Transactional(typeof(EntitiesDbContext))]
    [WolverinePost("/api/entities/{id}/hold")]
    [ProducesResponseType(typeof(EntityMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> SetHold(
        Guid id,
        SetEntityHoldRequest request,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (entity, actor, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        entity!.LegalHold = request.Hold;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor!.Value.Org,
            actor!.Value.Audit,
            request.Hold ? "entity.hold_set" : "entity.hold_cleared",
            new { entity.Id }
        );
        return Results.Ok(Mutated(entity));
    }

    [Transactional(typeof(EntitiesDbContext))]
    [WolverineDelete("/api/entities/{id}")]
    [ProducesResponseType(typeof(EntityMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Delete(
        Guid id,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (entity, actor, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (entity!.LegalHold)
            return ApiErrors.Conflict("entity is under legal hold");
        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = entity.DeletedAt.Value;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor!.Value.Org,
            actor!.Value.Audit,
            "entity.deleted",
            new { entity.Id }
        );
        return Results.Ok(Mutated(entity));
    }

    [Transactional(typeof(EntitiesDbContext))]
    [WolverinePost("/api/entities/{id}/restore")]
    [ProducesResponseType(typeof(EntityMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Restore(
        Guid id,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.EntitiesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.EntitiesManage).ToResult();
        var entity = await db
            .Entities.IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter])
            .FirstOrDefaultAsync(e => e.Id == id && e.DeletedAt != null, ct);
        if (entity is null)
            return Results.NotFound();
        var now = time.GetUtcNow();
        entity.DeletedAt = null;
        // a restored record that has already expired would be re-trashed by
        // the next sweep: give it a fresh window to be reviewed in
        if (entity.ExpiresAt <= now)
            entity.ExpiresAt = now.AddDays(30);
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(actor.Org, actor.Audit, "entity.restored", new { entity.Id });
        return Results.Ok(Mutated(entity));
    }

    /// <summary>Run the org's retention sweep now (the worker runs it daily).</summary>
    [Transactional(typeof(EntitiesDbContext))]
    [WolverinePost("/api/entities/retention/sweep")]
    [ProducesResponseType(typeof(RetentionSweepQueued), StatusCodes.Status200OK)]
    public static async Task<IResult> Sweep(
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.EntitiesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.EntitiesManage).ToResult();
        var now = time.GetUtcNow();
        var pending = await db.Entities.CountAsync(e => e.ExpiresAt <= now && !e.LegalHold, ct);
        await bus.PublishAsync(
            new ExpireEntities(),
            new DeliveryOptions { TenantId = actor.Org.Value.ToString() }
        );
        return Results.Ok(new RetentionSweepQueued(now, pending));
    }

    internal static async Task<(Entity?, ActorRef?, IResult?)> LoadForManage(
        Guid id,
        EntitiesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return (null, null, Results.Unauthorized());
        if (!await scopes.CanAsync(accessor.Current, Capabilities.EntitiesManage, ct))
            return (null, null, Results.Unauthorized());
        var entity = await db.Entities.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is null ? (null, null, Results.NotFound()) : (entity, actor, null);
    }

    internal static EntityMutated Mutated(Entity entity) =>
        new(
            entity.Id,
            entity.Status,
            entity.ExpiresAt,
            entity.LegalHold,
            entity.DeletedAt,
            entity.UpdatedAt
        );

    private static string? Validate(
        string displayName,
        Dictionary<string, string>? descriptors,
        DateTimeOffset expiresAt,
        DateTimeOffset now
    )
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "an entity needs a display name";
        if (displayName.Trim().Length > 200)
            return "display name is limited to 200 characters";
        if (!EntityRetention.IsAllowed(expiresAt, now))
            return $"retention cannot exceed {EntityRetention.MaxDays} days";
        if (descriptors is { Count: > 40 })
            return "at most 40 descriptors";
        if (
            descriptors is not null
            && descriptors.Any(d =>
                string.IsNullOrWhiteSpace(d.Key)
                || d.Key.Length > 40
                || (d.Value?.Length ?? 0) > 200
            )
        )
            return "descriptor keys are limited to 40 characters and values to 200";
        return null;
    }

    private static Dictionary<string, string> CleanDescriptors(
        Dictionary<string, string>? descriptors
    ) =>
        (descriptors ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d.Value))
            .ToDictionary(d => d.Key.Trim().ToLowerInvariant(), d => d.Value.Trim());

    private static Dictionary<string, string> Descriptors(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    private static string[] CleanAliases(string[]? aliases) =>
        aliases is null
            ? []
            : aliases.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct().Take(20).ToArray();
}
