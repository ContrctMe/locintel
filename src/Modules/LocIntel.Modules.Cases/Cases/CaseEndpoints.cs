using LocIntel.Contracts;
using LocIntel.Contracts.Entities;
using LocIntel.Contracts.Incidents;
using LocIntel.Contracts.Storage;
using LocIntel.Modules.Cases.Cases.Api;
using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>
/// Investigations. cases:read + membership-or-scope sees; members work
/// (tasks, notes, evidence); cases:manage runs the case (links, members,
/// close, hold, trash). Legal hold cascades to every evidence file over
/// the outbox, and the package export is itself a custody event.
/// </summary>
public static class CaseEndpoints
{
    [NonTransactional]
    [WolverineGet("/api/cases")]
    [ProducesResponseType(typeof(CaseListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        CaseStatus? status,
        CasePriority? priority,
        Guid? incidentId,
        Guid? entityId,
        bool? mine,
        string? q,
        bool? trash,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.CasesRead, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var manage = await scopes.CanAsync(accessor.Current, Capabilities.CasesManage, ct);
        if (trash is true && !manage)
            return new GateOutcome.Forbidden(Capabilities.CasesManage).ToResult();
        var query = CaseVisibility.Visible(db, scope, accessor.Current, manage);
        if (trash is true)
            query = query
                .IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter])
                .Where(c => c.DeletedAt != null);
        if (status is { } s)
            query = query.Where(c => c.Status == s);
        if (priority is { } p)
            query = query.Where(c => c.Priority == p);
        if (incidentId is { } incident)
            query = query.Where(c =>
                db.Incidents.Any(i => i.CaseId == c.Id && i.IncidentId == incident)
            );
        if (entityId is { } entity)
            query = query.Where(c =>
                db.Entities.Any(e => e.CaseId == c.Id && e.EntityId == entity)
            );
        if (mine is true)
            query = query.Where(c => db.Members.Any(m => m.CaseId == c.Id && m.UserId == actor.Id));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = $"%{q.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Title, needle) || EF.Functions.ILike(c.Summary, needle)
            );
        }
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var rows = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .Skip(skip)
            .Take(take)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.Status,
                c.Priority,
                c.LeadId,
                IncidentCount = db.Incidents.Count(i => i.CaseId == c.Id),
                OpenTasks = db.Tasks.Count(t => t.CaseId == c.Id && t.DoneAt == null),
                c.LegalHold,
                c.UpdatedAt,
                c.DeletedAt,
            })
            .ToListAsync(ct);
        var labels = await actors.LabelsAsync(
            rows.Where(r => r.LeadId != null).Select(r => r.LeadId!.Value).Distinct().ToList(),
            ct
        );
        var items = rows.Select(r => new CaseSummary(
                r.Id,
                r.Title,
                r.Status,
                r.Priority,
                r.LeadId,
                r.LeadId is { } lead ? labels.GetValueOrDefault(lead) : null,
                r.IncidentCount,
                r.OpenTasks,
                r.LegalHold,
                r.UpdatedAt,
                r.DeletedAt
            ))
            .ToList();
        return Results.Ok(
            new CaseListResponse(
                items,
                total,
                skip + items.Count < total ? skip + items.Count : null
            )
        );
    }

    [Transactional(
        typeof(CasesDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/cases/{id}")]
    [ProducesResponseType(typeof(CaseDetail), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        IIncidentDirectory incidents,
        IEntityDirectory entities,
        IStoredFileLookup files,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseAccess.LoadAsync(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        return Results.Ok(await DetailAsync(access!, db, actors, incidents, entities, files, ct));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases")]
    [ProducesResponseType(typeof(CaseOpened), StatusCodes.Status200OK)]
    public static async Task<IResult> Open(
        OpenCaseRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IIncidentDirectory incidents,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.CasesManage, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            return ApiErrors.BadRequest("a case needs a title of up to 200 characters");

        var @case = new Case
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            Title = request.Title.Trim(),
            Summary = request.Summary?.Trim() ?? "",
            Priority = request.Priority,
            LeadId = actor.IsService ? null : actor.Id,
            CreatedBy = actor.Id,
        };
        db.Cases.Add(@case);
        if (!actor.IsService)
            db.Members.Add(
                new CaseMember
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = actor.Org,
                    CaseId = @case.Id,
                    UserId = actor.Id,
                    Role = CaseMemberRole.Lead,
                    AddedBy = actor.Id,
                }
            );
        foreach (var incidentId in (request.IncidentIds ?? []).Distinct())
        {
            var incident = await incidents.FindAsync(incidentId, ct);
            if (incident is null || !scope.Covers(incident.Path))
                return Results.NotFound();
            db.Incidents.Add(NewLink(@case, incident, actor));
        }
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            "case.opened",
            new
            {
                @case.Id,
                @case.Title,
                Priority = @case.Priority.ToString(),
            }
        );
        return Results.Ok(new CaseOpened(@case.Id));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverinePut("/api/cases/{id}")]
    [ProducesResponseType(typeof(CaseMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Update(
        Guid id,
        UpdateCaseRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            return ApiErrors.BadRequest("a case needs a title of up to 200 characters");
        var @case = access!.Case;
        if (
            request.LeadId is { } lead
            && !await db.Members.AnyAsync(m => m.CaseId == id && m.UserId == lead, ct)
        )
            return ApiErrors.BadRequest("the lead must be a member of the case");
        @case.Title = request.Title.Trim();
        @case.Summary = request.Summary?.Trim() ?? "";
        @case.Priority = request.Priority;
        @case.LeadId = request.LeadId;
        @case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            "case.updated",
            new { @case.Id }
        );
        return Results.Ok(Mutated(@case));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/close")]
    [ProducesResponseType(typeof(CaseMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Close(
        Guid id,
        CloseCaseRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var @case = access!.Case;
        if (@case.Status == CaseStatus.Closed)
            return ApiErrors.Conflict("already closed");
        @case.Status = CaseStatus.Closed;
        @case.Disposition = request.Disposition;
        @case.ClosedAt = DateTimeOffset.UtcNow;
        @case.ClosedBy = access.Actor.Id;
        @case.ClosureNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        @case.UpdatedAt = @case.ClosedAt.Value;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            "case.closed",
            new { @case.Id, Disposition = @case.Disposition.ToString() }
        );
        return Results.Ok(Mutated(@case));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/reopen")]
    [ProducesResponseType(typeof(CaseMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Reopen(
        Guid id,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var @case = access!.Case;
        if (@case.Status == CaseStatus.Open)
            return ApiErrors.Conflict("already open");
        @case.Status = CaseStatus.Open;
        @case.Disposition = null;
        @case.ClosedAt = null;
        @case.ClosedBy = null;
        @case.ClosureNote = null;
        @case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            "case.reopened",
            new { @case.Id }
        );
        return Results.Ok(Mutated(@case));
    }

    /// <summary>Hold cascades to every evidence file (Storage applies it over the outbox); release does too.</summary>
    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/hold")]
    [ProducesResponseType(typeof(CaseMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> SetHold(
        Guid id,
        SetCaseHoldRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var @case = access!.Case;
        if (@case.LegalHold == request.Hold)
            return Results.Ok(Mutated(@case));
        @case.LegalHold = request.Hold;
        @case.UpdatedAt = DateTimeOffset.UtcNow;
        var fileIds = await db
            .Evidence.Where(e => e.CaseId == id)
            .Select(e => e.FileId)
            .ToListAsync(ct);
        foreach (var fileId in fileIds)
            CustodyLog.Record(
                db,
                @case,
                fileId,
                request.Hold ? CustodyAction.HoldPlaced : CustodyAction.HoldReleased,
                access.Actor
            );
        await db.SaveChangesAsync(ct);
        var reason = $"case {@case.Id}";
        foreach (var fileId in fileIds)
            await bus.PublishAsync(
                new FileHoldRequested(fileId, request.Hold, reason),
                new DeliveryOptions { TenantId = @case.OrgId.Value.ToString() }
            );
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            request.Hold ? "case.hold_set" : "case.hold_cleared",
            new { @case.Id, Files = fileIds.Count }
        );
        return Results.Ok(Mutated(@case));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverineDelete("/api/cases/{id}")]
    [ProducesResponseType(typeof(CaseMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Delete(
        Guid id,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var @case = access!.Case;
        if (@case.LegalHold)
            return ApiErrors.Conflict("case is under legal hold");
        if (@case.DeletedAt is not null)
            return Results.NotFound();
        @case.DeletedAt = DateTimeOffset.UtcNow;
        @case.UpdatedAt = @case.DeletedAt.Value;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            "case.deleted",
            new { @case.Id }
        );
        return Results.Ok(Mutated(@case));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/restore")]
    [ProducesResponseType(typeof(CaseMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Restore(
        Guid id,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var @case = access!.Case;
        if (@case.DeletedAt is null)
            return Results.NotFound();
        @case.DeletedAt = null;
        @case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            "case.restored",
            new { @case.Id }
        );
        return Results.Ok(Mutated(@case));
    }

    [Transactional(
        typeof(CasesDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/cases/{id}/custody")]
    [ProducesResponseType(typeof(CaseCustodyResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Custody(
        Guid id,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseAccess.LoadAsync(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        return Results.Ok(new CaseCustodyResponse(id, await CustodyAsync(id, db, actors, ct)));
    }

    /// <summary>The prosecution package; exporting is itself a custody event on every file.</summary>
    [Transactional(
        typeof(CasesDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/cases/{id}/package")]
    [ProducesResponseType(typeof(CasePackage), StatusCodes.Status200OK)]
    public static async Task<IResult> Package(
        Guid id,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        IIncidentDirectory incidents,
        IEntityDirectory entities,
        IStoredFileLookup files,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var @case = access!.Case;
        var fileIds = await db
            .Evidence.Where(e => e.CaseId == id)
            .Select(e => e.FileId)
            .ToListAsync(ct);
        foreach (var fileId in fileIds)
            CustodyLog.Record(db, @case, fileId, CustodyAction.Exported, access.Actor, "package");
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            "case.package_exported",
            new { @case.Id, Files = fileIds.Count }
        );
        var detail = await DetailAsync(access, db, actors, incidents, entities, files, ct);
        var labels = await actors.LabelsAsync([access.Actor.Id], ct);
        return Results.Ok(
            new CasePackage(
                DateTimeOffset.UtcNow,
                access.Actor.Id,
                labels.GetValueOrDefault(access.Actor.Id),
                detail,
                await CustodyAsync(id, db, actors, ct)
            )
        );
    }

    internal static async Task<(CaseAccess?, IResult?)> LoadForManage(
        Guid id,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseAccess.LoadAsync(id, db, accessor, scopes, ct);
        if (error is not null)
            return (null, error);
        return access!.Manage
            ? (access, null)
            : (null, new GateOutcome.Forbidden(Capabilities.CasesManage).ToResult());
    }

    internal static async Task<(CaseAccess?, IResult?)> LoadForWork(
        Guid id,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseAccess.LoadAsync(id, db, accessor, scopes, ct);
        if (error is not null)
            return (null, error);
        if (access!.Case.DeletedAt is not null)
            return (null, Results.NotFound());
        return access.CanWork
            ? (access, null)
            : (null, new GateOutcome.Forbidden(Capabilities.CasesManage).ToResult());
    }

    internal static CaseIncident NewLink(Case @case, IncidentInfo incident, ActorRef actor) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrgId = @case.OrgId,
            CaseId = @case.Id,
            IncidentId = incident.Id,
            SiteId = incident.SiteId,
            Path = new LTree(incident.Path),
            AddedBy = actor.Id,
        };

    internal static CaseMutated Mutated(Case @case) =>
        new(
            @case.Id,
            @case.Status,
            @case.Priority,
            @case.LegalHold,
            @case.DeletedAt,
            @case.UpdatedAt
        );

    private static async Task<IReadOnlyList<CustodyEventView>> CustodyAsync(
        Guid caseId,
        CasesDbContext db,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        var events = await db
            .Custody.Where(c => c.CaseId == caseId)
            .OrderBy(c => c.At)
            .ToListAsync(ct);
        var labels = await actors.LabelsAsync(
            events.Select(e => e.ActorId).Distinct().ToList(),
            ct
        );
        return events
            .Select(e => new CustodyEventView(
                e.Id,
                e.FileId,
                e.Action,
                e.ActorId,
                e.ActorTier,
                labels.GetValueOrDefault(e.ActorId),
                e.Detail,
                e.At
            ))
            .ToList();
    }

    private static async Task<CaseDetail> DetailAsync(
        CaseAccess access,
        CasesDbContext db,
        IActorDirectory actors,
        IIncidentDirectory incidents,
        IEntityDirectory entities,
        IStoredFileLookup files,
        CancellationToken ct
    )
    {
        var @case = access.Case;
        var id = @case.Id;
        var incidentLinks = await db
            .Incidents.Where(i => i.CaseId == id)
            .OrderBy(i => i.AddedAt)
            .ToListAsync(ct);
        var entityLinks = await db
            .Entities.Where(e => e.CaseId == id)
            .OrderBy(e => e.AddedAt)
            .ToListAsync(ct);
        var members = await db
            .Members.Where(m => m.CaseId == id)
            .OrderBy(m => m.AddedAt)
            .ToListAsync(ct);
        var tasks = await db
            .Tasks.Where(t => t.CaseId == id)
            .OrderBy(t => t.DoneAt != null)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
        var notes = await db
            .Notes.Where(n => n.CaseId == id)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);
        var evidence = await db
            .Evidence.Where(e => e.CaseId == id)
            .OrderBy(e => e.AddedAt)
            .ToListAsync(ct);
        var custodyCount = await db.Custody.CountAsync(c => c.CaseId == id, ct);

        var actorIds = members
            .Select(m => m.UserId)
            .Concat(tasks.Where(t => t.AssigneeId != null).Select(t => t.AssigneeId!.Value))
            .Concat(notes.Select(n => n.AuthorId))
            .Concat(evidence.Select(e => e.AddedBy));
        if (@case.LeadId is { } leadId)
            actorIds = actorIds.Append(leadId);
        var labels = await actors.LabelsAsync(actorIds.Distinct().ToList(), ct);

        var incidentViews = new List<CaseIncidentView>(incidentLinks.Count);
        foreach (var link in incidentLinks)
        {
            var incident = await incidents.FindAsync(link.IncidentId, ct);
            incidentViews.Add(
                new CaseIncidentView(
                    link.Id,
                    link.IncidentId,
                    link.SiteId,
                    incident?.Title,
                    incident?.Category,
                    incident?.Status,
                    incident?.OccurredAt,
                    link.AddedAt
                )
            );
        }
        // the need-to-know gate applies through the directory: what the
        // reader may not open, they see only as a restricted placeholder
        var visibleEntities = await entities.LookupVisibleAsync(
            entityLinks.Select(e => e.EntityId).ToList(),
            ct
        );
        var entityViews = entityLinks
            .Select(link =>
                visibleEntities.TryGetValue(link.EntityId, out var info)
                    ? new CaseEntityView(
                        link.Id,
                        link.EntityId,
                        info.DisplayName,
                        info.Kind,
                        info.Status,
                        link.Note,
                        false,
                        link.AddedAt
                    )
                    : new CaseEntityView(link.Id, null, null, null, null, null, true, link.AddedAt)
            )
            .ToList();
        var storedFiles = await files.GetManyAsync(evidence.Select(e => e.FileId).ToArray(), ct);
        var evidenceViews = new List<CaseEvidenceView>(evidence.Count);
        foreach (var item in evidence)
        {
            var file = storedFiles.GetValueOrDefault(item.FileId);
            evidenceViews.Add(
                new CaseEvidenceView(
                    item.Id,
                    item.FileId,
                    file?.Name,
                    file?.ContentType,
                    file?.Status,
                    item.Label,
                    item.AddedBy,
                    labels.GetValueOrDefault(item.AddedBy),
                    item.AddedAt
                )
            );
        }
        return new CaseDetail(
            @case.Id,
            @case.Title,
            @case.Summary,
            @case.Status,
            @case.Priority,
            @case.LeadId,
            @case.LeadId is { } lead ? labels.GetValueOrDefault(lead) : null,
            @case.Disposition,
            @case.ClosedAt,
            @case.ClosureNote,
            @case.LegalHold,
            access.Manage,
            access.CanWork,
            @case.CreatedAt,
            @case.UpdatedAt,
            @case.DeletedAt,
            incidentViews,
            entityViews,
            members
                .Select(m => new CaseMemberView(
                    m.Id,
                    m.UserId,
                    labels.GetValueOrDefault(m.UserId),
                    m.Role,
                    m.AddedAt
                ))
                .ToList(),
            tasks
                .Select(t => new CaseTaskView(
                    t.Id,
                    t.Title,
                    t.AssigneeId,
                    t.AssigneeId is { } a ? labels.GetValueOrDefault(a) : null,
                    t.DueAt,
                    t.DoneAt,
                    t.CreatedAt
                ))
                .ToList(),
            notes
                .Select(n => new CaseNoteView(
                    n.Id,
                    n.AuthorId,
                    labels.GetValueOrDefault(n.AuthorId),
                    n.Body,
                    n.CreatedAt
                ))
                .ToList(),
            evidenceViews,
            custodyCount
        );
    }
}
