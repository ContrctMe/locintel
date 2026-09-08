using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Alerts.Alerts;
using LocIntel.Modules.Alerts.Bulletins.Api;
using LocIntel.Modules.Alerts.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Alerts.Bulletins;

/// <summary>
/// BOLOs and advisories. alerts:manage issues (within the issuer's own
/// scope) and withdraws; alerts:read sees what overlaps their scope and
/// acknowledges. Issuing also drops an alert in the feed and emails the
/// org's managers.
/// </summary>
public static class BulletinEndpoints
{
    public const int DefaultDays = 14;
    public const int MaxDays = 90;

    [Transactional(
        typeof(AlertsDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/bulletins")]
    [ProducesResponseType(typeof(BulletinListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        AlertsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        TimeProvider time,
        bool? includeInactive,
        BulletinKind? kind,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { UserId: var userId })
            return Results.Unauthorized();
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.AlertsRead, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var now = time.GetUtcNow();
        var query = ScopeOverlap.Bulletins(db.Bulletins, scope);
        if (includeInactive is not true)
            query = query.Where(b => b.Status == BulletinStatus.Active && b.ExpiresAt > now);
        if (kind is { } k)
            query = query.Where(b => b.Kind == k);
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var rows = await query
            .OrderByDescending(b => b.Severity)
            .ThenByDescending(b => b.IssuedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        var views = await ViewsAsync(rows, userId, db, actors, now, ct);
        return Results.Ok(
            new BulletinListResponse(
                views,
                total,
                skip + rows.Count < total ? skip + rows.Count : null
            )
        );
    }

    [Transactional(
        typeof(AlertsDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/bulletins/{id}")]
    [ProducesResponseType(typeof(BulletinDetail), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        AlertsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { UserId: var userId })
            return Results.Unauthorized();
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.AlertsRead, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var bulletin = await ScopeOverlap
            .Bulletins(db.Bulletins, scope)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bulletin is null)
            return Results.NotFound();
        var manage = await scopes.CanAsync(accessor.Current, Capabilities.AlertsManage, ct);
        var view = (await ViewsAsync([bulletin], userId, db, actors, time.GetUtcNow(), ct))[0];
        IReadOnlyList<AcknowledgementView>? acks = null;
        if (manage)
        {
            var rows = await db
                .Acknowledgements.Where(a => a.BulletinId == id)
                .OrderBy(a => a.AcknowledgedAt)
                .ToListAsync(ct);
            var labels = await actors.LabelsAsync(
                rows.Select(a => a.UserId).Distinct().ToList(),
                ct
            );
            acks = rows.Select(a => new AcknowledgementView(
                    a.Id,
                    a.UserId,
                    labels.GetValueOrDefault(a.UserId),
                    a.SiteId,
                    a.Note,
                    a.AcknowledgedAt
                ))
                .ToList();
        }
        return Results.Ok(new BulletinDetail(view, acks));
    }

    [Transactional(typeof(AlertsDbContext))]
    [WolverinePost("/api/bulletins")]
    [ProducesResponseType(typeof(BulletinIssued), StatusCodes.Status200OK)]
    public static async Task<IResult> Issue(
        IssueBulletinRequest request,
        AlertsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.AlertsManage, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            return ApiErrors.BadRequest("a bulletin needs a title of up to 200 characters");
        if (string.IsNullOrWhiteSpace(request.Body))
            return ApiErrors.BadRequest("a bulletin needs a body");
        var now = time.GetUtcNow();
        var expiresAt = request.ExpiresAt ?? now.AddDays(DefaultDays);
        if (expiresAt <= now || expiresAt > now.AddDays(MaxDays))
            return ApiErrors.BadRequest($"a bulletin expires between now and {MaxDays} days out");
        LTree? scopePath = null;
        if (!string.IsNullOrWhiteSpace(request.ScopePath))
        {
            var path = request.ScopePath.Trim();
            if (!scope.Covers(path))
                return Results.NotFound();
            if (scope is NodeScope.Subtrees)
                scopePath = new LTree(path);
            else
                scopePath = new LTree(path);
        }
        else if (scope is NodeScope.Subtrees)
            return ApiErrors.BadRequest("your scope is a subtree: target it explicitly");

        var bulletin = new Bulletin
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            Kind = request.Kind,
            Severity = request.Severity,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            ScopePath = scopePath,
            EntityId = request.EntityId,
            IncidentId = request.IncidentId,
            CaseId = request.CaseId,
            IssuedBy = actor.Id,
            ExpiresAt = expiresAt,
        };
        db.Bulletins.Add(bulletin);
        db.Alerts.Add(
            new Alert
            {
                Id = Guid.CreateVersion7(),
                OrgId = actor.Org,
                Kind = AlertKind.BulletinIssued,
                Severity = bulletin.Severity,
                Title = $"{bulletin.Kind.ToString().ToUpperInvariant()}: {bulletin.Title}",
                Body = bulletin.Body.Length > 2000 ? bulletin.Body[..2000] : bulletin.Body,
                Path = scopePath,
                BulletinId = bulletin.Id,
                EntityId = bulletin.EntityId,
                IncidentId = bulletin.IncidentId,
            }
        );
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new SendOrgNotice(
                $"{bulletin.Kind.ToString().ToUpperInvariant()}: {bulletin.Title}",
                [
                    bulletin.Body,
                    $"Expires {bulletin.ExpiresAt:u}.",
                    "Acknowledge it in the console.",
                ],
                "alerts",
                $"{bulletin.Kind.ToString().ToUpperInvariant()}: {bulletin.Title}"
            ),
            new DeliveryOptions { TenantId = actor.Org.Value.ToString() }
        );
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            "bulletin.issued",
            new
            {
                bulletin.Id,
                Kind = bulletin.Kind.ToString(),
                bulletin.Title,
            }
        );
        return Results.Ok(new BulletinIssued(bulletin.Id, bulletin.ExpiresAt));
    }

    [Transactional(typeof(AlertsDbContext))]
    [WolverinePost("/api/bulletins/{id}/withdraw")]
    [ProducesResponseType(typeof(BulletinMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Withdraw(
        Guid id,
        AlertsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.AlertsManage, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var bulletin = await ScopeOverlap
            .Bulletins(db.Bulletins, scope)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bulletin is null)
            return Results.NotFound();
        if (bulletin.Status == BulletinStatus.Withdrawn)
            return ApiErrors.Conflict("already withdrawn");
        bulletin.Status = BulletinStatus.Withdrawn;
        bulletin.WithdrawnAt = DateTimeOffset.UtcNow;
        bulletin.WithdrawnBy = actor.Id;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(actor.Org, actor.Audit, "bulletin.withdrawn", new { bulletin.Id });
        return Results.Ok(new BulletinMutated(bulletin.Id, bulletin.Status, bulletin.WithdrawnAt));
    }

    [Transactional(typeof(AlertsDbContext))]
    [WolverinePost("/api/bulletins/{id}/acknowledge")]
    [ProducesResponseType(typeof(BulletinAcknowledged), StatusCodes.Status200OK)]
    public static async Task<IResult> Acknowledge(
        Guid id,
        AcknowledgeBulletinRequest request,
        AlertsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org, UserId: var userId })
            return Results.Unauthorized();
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.AlertsRead, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var now = time.GetUtcNow();
        var bulletin = await ScopeOverlap
            .Bulletins(db.Bulletins, scope)
            .FirstOrDefaultAsync(
                b => b.Id == id && b.Status == BulletinStatus.Active && b.ExpiresAt > now,
                ct
            );
        if (bulletin is null)
            return Results.NotFound();
        var ack = await db.Acknowledgements.FirstOrDefaultAsync(
            a => a.BulletinId == id && a.UserId == userId,
            ct
        );
        if (ack is null)
        {
            ack = new BulletinAcknowledgement
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                BulletinId = id,
                UserId = userId,
                SiteId = request.SiteId,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            };
            db.Acknowledgements.Add(ack);
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(new BulletinAcknowledged(ack.Id, ack.AcknowledgedAt));
    }

    private static async Task<List<BulletinView>> ViewsAsync(
        IReadOnlyList<Bulletin> rows,
        Guid userId,
        AlertsDbContext db,
        IActorDirectory actors,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var ids = rows.Select(b => b.Id).ToArray();
        var mine = await db
            .Acknowledgements.Where(a => ids.Contains(a.BulletinId) && a.UserId == userId)
            .Select(a => a.BulletinId)
            .ToListAsync(ct);
        var counts = await db
            .Acknowledgements.Where(a => ids.Contains(a.BulletinId))
            .GroupBy(a => a.BulletinId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var labels = await actors.LabelsAsync(rows.Select(b => b.IssuedBy).Distinct().ToList(), ct);
        return rows.Select(b => new BulletinView(
                b.Id,
                b.Kind,
                b.Severity,
                b.Title,
                b.Body,
                b.ScopePath?.ToString(),
                b.EntityId,
                b.IncidentId,
                b.CaseId,
                b.IssuedBy,
                labels.GetValueOrDefault(b.IssuedBy),
                b.IssuedAt,
                b.ExpiresAt,
                b.Status,
                b.Status == BulletinStatus.Active && b.ExpiresAt > now,
                mine.Contains(b.Id),
                counts.GetValueOrDefault(b.Id)
            ))
            .ToList();
    }
}
