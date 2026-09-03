using LocIntel.Contracts;
using LocIntel.Modules.Alerts.Alerts.Api;
using LocIntel.Modules.Alerts.Bulletins;
using LocIntel.Modules.Alerts.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Alerts.Alerts;

/// <summary>The feed: scope-filtered, read state per user.</summary>
public static class AlertEndpoints
{
    [Transactional(typeof(AlertsDbContext))]
    [WolverineGet("/api/alerts")]
    [ProducesResponseType(typeof(AlertListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        AlertsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        bool? unreadOnly,
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
        var visible = ScopeOverlap.Alerts(db.Alerts, scope);
        var unread = await visible.CountAsync(
            a => !db.Reads.Any(r => r.AlertId == a.Id && r.UserId == userId),
            ct
        );
        var query = unreadOnly is true
            ? visible.Where(a => !db.Reads.Any(r => r.AlertId == a.Id && r.UserId == userId))
            : visible;
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip(skip)
            .Take(take)
            .Select(a => new AlertView(
                a.Id,
                a.Kind,
                a.Severity,
                a.Title,
                a.Body,
                a.IncidentId,
                a.BulletinId,
                a.EntityId,
                a.CreatedAt,
                db.Reads.Where(r => r.AlertId == a.Id && r.UserId == userId)
                    .Select(r => (DateTimeOffset?)r.ReadAt)
                    .FirstOrDefault()
            ))
            .ToListAsync(ct);
        return Results.Ok(
            new AlertListResponse(
                items,
                total,
                skip + items.Count < total ? skip + items.Count : null,
                unread
            )
        );
    }

    [Transactional(typeof(AlertsDbContext))]
    [WolverinePost("/api/alerts/{id}/read")]
    [ProducesResponseType(typeof(AlertMarkedRead), StatusCodes.Status200OK)]
    public static async Task<IResult> MarkRead(
        Guid id,
        AlertsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org, UserId: var userId })
            return Results.Unauthorized();
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.AlertsRead, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var alert = await ScopeOverlap
            .Alerts(db.Alerts, scope)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert is null)
            return Results.NotFound();
        var read = await db.Reads.FirstOrDefaultAsync(
            r => r.AlertId == id && r.UserId == userId,
            ct
        );
        if (read is null)
        {
            read = new AlertRead
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                AlertId = id,
                UserId = userId,
            };
            db.Reads.Add(read);
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(new AlertMarkedRead(id, read.ReadAt));
    }

    [Transactional(typeof(AlertsDbContext))]
    [WolverineGet("/api/alerts/summary")]
    [ProducesResponseType(typeof(AlertSummary), StatusCodes.Status200OK)]
    public static async Task<IResult> Summary(
        AlertsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { UserId: var userId })
            return Results.Unauthorized();
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.AlertsRead, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var now = time.GetUtcNow();
        var unread = await ScopeOverlap
            .Alerts(db.Alerts, scope)
            .CountAsync(a => !db.Reads.Any(r => r.AlertId == a.Id && r.UserId == userId), ct);
        var active = ScopeOverlap
            .Bulletins(db.Bulletins, scope)
            .Where(b => b.Status == BulletinStatus.Active && b.ExpiresAt > now);
        var activeCount = await active.CountAsync(ct);
        var unacknowledged = await active.CountAsync(
            b => !db.Acknowledgements.Any(a => a.BulletinId == b.Id && a.UserId == userId),
            ct
        );
        return Results.Ok(new AlertSummary(unread, activeCount, unacknowledged));
    }
}
