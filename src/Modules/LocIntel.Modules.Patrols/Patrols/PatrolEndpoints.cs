using LocIntel.Contracts;
using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Patrols.Data;
using LocIntel.Modules.Patrols.Patrols.Api;
using LocIntel.Modules.Patrols.Routes;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Scheduling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Patrols.Patrols;

/// <summary>
/// Runs. "Today" is the SITE's business date (ADR 26); expected rounds are
/// the schedules expanded in the site's zone for that day (ADR 27); runs
/// stamp the site's path and business date at start (ADR 2/26).
/// patrols:perform starts, scans, ends within scope; patrols:read sees.
/// </summary>
public static class PatrolEndpoints
{
    [Transactional(typeof(PatrolsDbContext))]
    [WolverineGet("/api/patrols/today")]
    [ProducesResponseType(typeof(PatrolDayResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Today(
        Guid siteId,
        DateOnly? date,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISiteDirectory sites,
        IActorDirectory actors,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.PatrolsRead, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var site = await sites.FindAsync(siteId, ct);
        if (site is null || !scope.Covers(site.Path))
            return Results.NotFound();
        var day = date ?? BusinessDate.For(time.GetUtcNow(), site.TimeZone);
        var (expected, patrols) = await DayAsync(site, day, db, actors, ct);
        return Results.Ok(new PatrolDayResponse(day, site.Name, expected, patrols));
    }

    [Transactional(typeof(PatrolsDbContext))]
    [WolverineGet("/api/patrols/report")]
    [ProducesResponseType(typeof(DailyActivityReport), StatusCodes.Status200OK)]
    public static async Task<IResult> Report(
        Guid siteId,
        DateOnly? date,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISiteDirectory sites,
        IActorDirectory actors,
        IIncidentDirectory incidents,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.PatrolsRead, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var site = await sites.FindAsync(siteId, ct);
        if (site is null || !scope.Covers(site.Path))
            return Results.NotFound();
        var day = date ?? BusinessDate.For(time.GetUtcNow(), site.TimeZone);
        var (expected, patrols) = await DayAsync(site, day, db, actors, ct);
        var missed = expected
            .Where(e => e.PatrolId is null || e.Status == PatrolStatus.Abandoned)
            .ToList();
        var lines = (await incidents.ListForDayAsync(siteId, day, ct))
            .Select(i => new IncidentLine(
                i.Id,
                i.Title,
                i.Category,
                i.Severity,
                i.Status,
                i.OccurredAt
            ))
            .ToList();
        return Results.Ok(
            new DailyActivityReport(
                day,
                siteId,
                site.Name,
                expected.Count,
                expected.Count(e => e.Status == PatrolStatus.Completed),
                missed,
                patrols,
                lines
            )
        );
    }

    [Transactional(typeof(PatrolsDbContext))]
    [WolverineGet("/api/patrols/{id}")]
    [ProducesResponseType(typeof(PatrolView), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.PatrolsRead, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var patrol = await db.Patrols.InScope(scope).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (patrol is null)
            return Results.NotFound();
        var route = await db
            .Routes.IgnoreQueryFilters([ModuleDbContext.TenantFilter])
            .Where(r => r.OrgId == patrol.OrgId && r.Id == patrol.RouteId)
            .FirstAsync(ct);
        var scans = await db
            .Scans.Where(s => s.PatrolId == id)
            .OrderBy(s => s.ScannedAt)
            .ToListAsync(ct);
        var labels = await actors.LabelsAsync([patrol.StartedBy], ct);
        return Results.Ok(View(patrol, route, scans, labels));
    }

    [Transactional(typeof(PatrolsDbContext))]
    [WolverinePost("/api/patrols")]
    [ProducesResponseType(typeof(PatrolStarted), StatusCodes.Status200OK)]
    public static async Task<IResult> Start(
        StartPatrolRequest request,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISiteDirectory sites,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.PatrolsPerform, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var route = await db
            .Routes.InScope(scope)
            .FirstOrDefaultAsync(r => r.Id == request.RouteId && !r.Archived, ct);
        if (route is null)
            return Results.NotFound();
        var site = await sites.FindAsync(route.SiteId, ct);
        if (site is null)
            return Results.NotFound();
        var now = time.GetUtcNow();
        var patrol = new Patrol
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            RouteId = route.Id,
            SiteId = route.SiteId,
            Path = route.Path,
            BusinessDate = BusinessDate.For(now, site.TimeZone),
            ScheduledStartLocal = request.ScheduledStartLocal,
            StartedBy = actor.Id,
            StartedByTier = actor.Tier,
        };
        db.Patrols.Add(patrol);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            "patrol.started",
            new
            {
                patrol.Id,
                patrol.RouteId,
                patrol.SiteId,
                patrol.BusinessDate,
            }
        );
        return Results.Ok(new PatrolStarted(patrol.Id, patrol.BusinessDate));
    }

    [Transactional(typeof(PatrolsDbContext))]
    [WolverinePost("/api/patrols/{id}/scan")]
    [ProducesResponseType(typeof(ScanRecorded), StatusCodes.Status200OK)]
    public static async Task<IResult> Scan(
        Guid id,
        ScanRequest request,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (patrol, route, _, error) = await LoadForPerform(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (patrol!.Status != PatrolStatus.InProgress)
            return Results.Conflict(new { error = "the patrol has ended" });
        var code = request.Code?.Trim().ToUpperInvariant() ?? "";
        var checkpoints = RouteEndpoints.Checkpoints(route!);
        var checkpoint = checkpoints.FirstOrDefault(c => c.Code == code);
        if (checkpoint is null)
            return Results.NotFound();
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            return Results.BadRequest(new { error = "coordinates out of range" });
        double? distance =
            checkpoint.Latitude is { } clat
            && checkpoint.Longitude is { } clng
            && request.Latitude is { } lat
            && request.Longitude is { } lng
                ? Geo.DistanceMeters(clat, clng, lat, lng)
                : null;
        var scan = new PatrolScan
        {
            Id = Guid.CreateVersion7(),
            OrgId = patrol.OrgId,
            PatrolId = patrol.Id,
            Code = code,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            DistanceMeters = distance,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        };
        db.Scans.Add(scan);
        await db.SaveChangesAsync(ct);
        var scanned = await db
            .Scans.Where(s => s.PatrolId == id)
            .Select(s => s.Code)
            .Distinct()
            .CountAsync(ct);
        return Results.Ok(
            new ScanRecorded(
                scan.Id,
                distance,
                distance is { } d ? d <= Geo.GeofenceMeters : null,
                scanned,
                checkpoints.Length
            )
        );
    }

    [Transactional(typeof(PatrolsDbContext))]
    [WolverinePost("/api/patrols/{id}/end")]
    [ProducesResponseType(typeof(PatrolMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> End(
        Guid id,
        EndPatrolRequest request,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (patrol, _, actor, error) = await LoadForPerform(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (patrol!.Status != PatrolStatus.InProgress)
            return Results.Conflict(new { error = "the patrol has ended" });
        patrol.Status = PatrolStatus.Completed;
        patrol.EndedAt = DateTimeOffset.UtcNow;
        patrol.Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim();
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor!.Value.Org,
            actor!.Value.Audit,
            "patrol.completed",
            new { patrol.Id }
        );
        return Results.Ok(new PatrolMutated(patrol.Id, patrol.Status, patrol.EndedAt));
    }

    [Transactional(typeof(PatrolsDbContext))]
    [WolverinePost("/api/patrols/{id}/abandon")]
    [ProducesResponseType(typeof(PatrolMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Abandon(
        Guid id,
        AbandonPatrolRequest request,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (patrol, _, actor, error) = await LoadForPerform(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (patrol!.Status != PatrolStatus.InProgress)
            return Results.Conflict(new { error = "the patrol has ended" });
        patrol.Status = PatrolStatus.Abandoned;
        patrol.EndedAt = DateTimeOffset.UtcNow;
        patrol.Summary = string.IsNullOrWhiteSpace(request.Reason)
            ? "Abandoned"
            : $"Abandoned: {request.Reason.Trim()}";
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor!.Value.Org,
            actor!.Value.Audit,
            "patrol.abandoned",
            new { patrol.Id }
        );
        return Results.Ok(new PatrolMutated(patrol.Id, patrol.Status, patrol.EndedAt));
    }

    private static async Task<(Patrol?, PatrolRoute?, ActorRef?, IResult?)> LoadForPerform(
        Guid id,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return (null, null, null, Results.Unauthorized());
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.PatrolsPerform, ct);
        if (scope is NodeScope.None)
            return (null, null, null, Results.Unauthorized());
        var patrol = await db.Patrols.InScope(scope).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (patrol is null)
            return (null, null, null, Results.NotFound());
        var route = await db.Routes.FirstAsync(r => r.Id == patrol.RouteId, ct);
        return (patrol, route, actor, null);
    }

    /// <summary>Expected occurrences (schedules expanded in the site's zone for the day) matched to actual runs.</summary>
    private static async Task<(List<ExpectedPatrol>, List<PatrolView>)> DayAsync(
        SiteInfo site,
        DateOnly day,
        PatrolsDbContext db,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        var routes = await db.Routes.Where(r => r.SiteId == site.Id).ToListAsync(ct);
        var routeIds = routes.Select(r => r.Id).ToArray();
        var schedules = await db
            .Schedules.Where(s => routeIds.Contains(s.RouteId) && s.Active)
            .ToListAsync(ct);
        var patrols = await db
            .Patrols.Where(p => p.SiteId == site.Id && p.BusinessDate == day)
            .OrderBy(p => p.StartedAt)
            .ToListAsync(ct);
        var patrolIds = patrols.Select(p => p.Id).ToArray();
        var scans = await db
            .Scans.Where(s => patrolIds.Contains(s.PatrolId))
            .OrderBy(s => s.ScannedAt)
            .ToListAsync(ct);
        var labels = await actors.LabelsAsync(
            patrols.Select(p => p.StartedBy).Distinct().ToList(),
            ct
        );

        var zone = TimeZoneInfo.FindSystemTimeZoneById(site.TimeZone);
        var dayStart = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(
                day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                zone
            ),
            TimeSpan.Zero
        );
        var horizonStart = dayStart.AddDays(-1);
        var horizonEnd = dayStart.AddDays(2);
        var expected = new List<ExpectedPatrol>();
        foreach (var schedule in schedules)
        {
            var route = routes.First(r => r.Id == schedule.RouteId);
            if (route.Archived)
                continue;
            var end = schedule.StartLocal.Add(
                TimeSpan.FromMinutes(Math.Min(route.ExpectedMinutes, 1439)),
                out _
            );
            var occurrences = RecurrenceExpander.Expand(
                schedule.RRule,
                schedule.AnchorDate,
                schedule.StartLocal,
                end,
                site.TimeZone,
                schedule.ExDates,
                horizonStart,
                horizonEnd
            );
            foreach (var occurrence in occurrences.Where(o => o.LocalDate == day))
            {
                var match = patrols.FirstOrDefault(p =>
                    p.RouteId == route.Id && p.ScheduledStartLocal == schedule.StartLocal
                );
                expected.Add(
                    new ExpectedPatrol(
                        route.Id,
                        route.Name,
                        schedule.StartLocal,
                        occurrence.StartUtc,
                        match?.Id,
                        match?.Status
                    )
                );
            }
        }
        expected = expected.OrderBy(e => e.StartLocal).ThenBy(e => e.RouteName).ToList();
        var views = patrols
            .Select(p =>
                View(
                    p,
                    routes.First(r => r.Id == p.RouteId),
                    scans.Where(s => s.PatrolId == p.Id).ToList(),
                    labels
                )
            )
            .ToList();
        return (expected, views);
    }

    private static PatrolView View(
        Patrol patrol,
        PatrolRoute route,
        IReadOnlyList<PatrolScan> scans,
        IReadOnlyDictionary<Guid, string> labels
    )
    {
        var checkpoints = RouteEndpoints.Checkpoints(route);
        return new PatrolView(
            patrol.Id,
            patrol.RouteId,
            route.Name,
            patrol.SiteId,
            patrol.BusinessDate,
            patrol.ScheduledStartLocal,
            patrol.Status,
            patrol.StartedAt,
            patrol.StartedBy,
            labels.GetValueOrDefault(patrol.StartedBy),
            patrol.EndedAt,
            patrol.Summary,
            checkpoints.Length,
            scans.Select(s => s.Code).Distinct().Count(),
            scans
                .Select(s => new ScanView(
                    s.Id,
                    s.Code,
                    checkpoints.FirstOrDefault(c => c.Code == s.Code)?.Label,
                    s.ScannedAt,
                    s.DistanceMeters,
                    s.DistanceMeters is { } d ? d <= Geo.GeofenceMeters : null,
                    s.Note
                ))
                .ToList()
        );
    }
}
