using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Audit;

public sealed record SetAuditConfigRequest(bool LogGrants, bool LogReads);

public static class AuditEndpoints
{
    [Transactional(typeof(AuditDbContext))]
    [WolverineGet("/api/audit/{kind}")]
    public static async Task<IResult> Query(
        string kind,
        AuditDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        int? limit,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.AuditRead, ct)
        )
            return Results.Unauthorized();
        var take = Math.Clamp(limit ?? 50, 1, 500);
        // RLS scopes these queries; the explicit org predicate is belt to its suspenders
        object rows = kind switch
        {
            "changes" => await db
                .Changes.Where(a => a.OrgId == org.Value)
                .OrderByDescending(a => a.OccurredAt)
                .Take(take)
                .Select(a => new
                {
                    a.Id,
                    a.ActorTier,
                    a.ActorLabel,
                    a.SchemaName,
                    a.TableName,
                    a.RowId,
                    a.Operation,
                    diff = a.Diff,
                    a.OccurredAt,
                })
                .ToListAsync(ct),
            // events resolve WHO, not just which tier: the id was stamped at
            // write time, the label is looked up at read time so renames and
            // old rows stay correct (competitive review, finding 3)
            "events" => await EventsWithActorsAsync(db, actors, org, take, ct),
            "authz" => await db
                .AuthzDecisions.Where(a => a.OrgId == org.Value)
                .OrderByDescending(a => a.OccurredAt)
                .Take(take)
                .Select(a => new
                {
                    a.Id,
                    a.ActorTier,
                    a.ActorId,
                    a.Action,
                    a.Outcome,
                    a.ScopeSummary,
                    a.OccurredAt,
                })
                .ToListAsync(ct),
            "access" => await db
                .Accesses.Where(a => a.OrgId == org.Value)
                .OrderByDescending(a => a.OccurredAt)
                .Take(take)
                .Select(a => new
                {
                    a.Id,
                    a.ActorTier,
                    a.Method,
                    a.Path,
                    a.StatusCode,
                    a.OccurredAt,
                })
                .ToListAsync(ct),
            _ => null!,
        };
        return rows is null ? Results.NotFound() : Results.Ok(rows);
    }

    [Transactional(typeof(AuditDbContext))]
    [WolverineGet("/api/admin/audit-config")]
    public static async Task<IResult> GetConfig(
        AuditDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.AuditManage, ct)
        )
            return Results.Unauthorized();
        var config = await db.Configs.FirstOrDefaultAsync(ct);
        return Results.Ok(
            new
            {
                logGrants = config?.LogGrants ?? false,
                logReads = config?.LogReads ?? false,
                floor = new
                {
                    domainEvents = true,
                    authzDenials = true,
                    changeDiffs = true,
                },
            }
        );
    }

    /// <summary>Audit config is self-referential (ADR 12): changing it emits a domain audit event.</summary>
    [Transactional(typeof(AuditDbContext))]
    [WolverinePut("/api/admin/audit-config")]
    public static async Task<IResult> SetConfig(
        SetAuditConfigRequest request,
        AuditDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } org, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.AuditManage, ct)
        )
            return Results.Unauthorized();

        var config = await db.Configs.FirstOrDefaultAsync(ct);
        var before = new
        {
            logGrants = config?.LogGrants ?? false,
            logReads = config?.LogReads ?? false,
        };
        if (config is null)
        {
            config = new OrgAuditConfig { Id = Guid.CreateVersion7(), OrgId = org };
            db.Configs.Add(config);
        }
        config.LogGrants = request.LogGrants;
        config.LogReads = request.LogReads;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await bus.AuditAsync(
            org,
            AuditActor.User(userId),
            "audit.config_changed",
            new
            {
                before,
                after = new { request.LogGrants, request.LogReads },
                changedBy = userId,
            }
        );
        return Results.NoContent();
    }

    private static async Task<object> EventsWithActorsAsync(
        AuditDbContext db,
        IActorDirectory actors,
        OrgId org,
        int take,
        CancellationToken ct
    )
    {
        var rows = await db
            .DomainEvents.Where(a => a.OrgId == org.Value)
            .OrderByDescending(a => a.OccurredAt)
            .Take(take)
            .Select(a => new
            {
                a.Id,
                a.ActorTier,
                a.ActorId,
                a.EventName,
                payload = a.Payload,
                a.OccurredAt,
            })
            .ToListAsync(ct);
        var labels = await actors.LabelsAsync(
            rows.Where(r => r.ActorId is not null)
                .Select(r => r.ActorId!.Value)
                .Distinct()
                .ToList(),
            ct
        );
        return rows.Select(r => new
        {
            r.Id,
            r.ActorTier,
            actorLabel = r.ActorId is { } actorId ? labels.GetValueOrDefault(actorId) : null,
            r.EventName,
            r.payload,
            r.OccurredAt,
        });
    }
}
