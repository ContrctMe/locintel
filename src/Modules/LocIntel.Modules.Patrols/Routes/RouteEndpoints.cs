using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Patrols.Data;
using LocIntel.Modules.Patrols.Patrols;
using LocIntel.Modules.Patrols.Routes.Api;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Patrols.Routes;

/// <summary>Routes and their schedules (patrols:manage, within the site's scope); anyone with patrols:read lists them.</summary>
public static class RouteEndpoints
{
    [Transactional(typeof(PatrolsDbContext))]
    [WolverineGet("/api/patrols/routes")]
    [ProducesResponseType(typeof(RouteListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        Guid? siteId,
        bool? includeArchived,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.PatrolsRead, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var query = db.Routes.InScope(scope);
        if (siteId is { } site)
            query = query.Where(r => r.SiteId == site);
        if (includeArchived is not true)
            query = query.Where(r => !r.Archived);
        var routes = await query.OrderBy(r => r.Name).ToListAsync(ct);
        var ids = routes.Select(r => r.Id).ToArray();
        var schedules = await db
            .Schedules.Where(s => ids.Contains(s.RouteId))
            .OrderBy(s => s.StartLocal)
            .ToListAsync(ct);
        return Results.Ok(new RouteListResponse(routes.Select(r => View(r, schedules)).ToList()));
    }

    [Transactional(typeof(PatrolsDbContext))]
    [WolverinePost("/api/patrols/routes")]
    [ProducesResponseType(typeof(RouteCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateRouteRequest request,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISiteDirectory sites,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.PatrolsManage, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var site = await sites.FindAsync(request.SiteId, ct);
        if (site is null || !scope.Covers(site.Path))
            return Results.NotFound();
        if (Validate(request.Name, request.Checkpoints, request.ExpectedMinutes) is { } error)
            return Results.BadRequest(new { error });
        var route = new PatrolRoute
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            SiteId = site.Id,
            Path = new LTree(site.Path),
            Name = request.Name.Trim(),
            CheckpointsJson = JsonSerializer.Serialize(Clean(request.Checkpoints)),
            ExpectedMinutes = request.ExpectedMinutes ?? 30,
            CreatedBy = actor.Id,
        };
        db.Routes.Add(route);
        await db.SaveChangesAsync(ct);
        await PatrolAudit.PublishAsync(
            bus,
            actor,
            "patrol.route_created",
            new
            {
                route.Id,
                route.SiteId,
                route.Name,
            }
        );
        return Results.Ok(new RouteCreated(route.Id));
    }

    [Transactional(typeof(PatrolsDbContext))]
    [WolverinePut("/api/patrols/routes/{id}")]
    [ProducesResponseType(typeof(RouteMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Update(
        Guid id,
        UpdateRouteRequest request,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (route, actor, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (Validate(request.Name, request.Checkpoints, request.ExpectedMinutes) is { } invalid)
            return Results.BadRequest(new { error = invalid });
        route!.Name = request.Name.Trim();
        route.CheckpointsJson = JsonSerializer.Serialize(Clean(request.Checkpoints));
        route.ExpectedMinutes = request.ExpectedMinutes ?? route.ExpectedMinutes;
        route.Archived = request.Archived;
        route.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await PatrolAudit.PublishAsync(
            bus,
            actor!.Value,
            request.Archived ? "patrol.route_archived" : "patrol.route_updated",
            new { route.Id }
        );
        return Results.Ok(new RouteMutated(route.Id, route.Archived, route.UpdatedAt));
    }

    [Transactional(typeof(PatrolsDbContext))]
    [WolverinePost("/api/patrols/routes/{id}/schedules")]
    [ProducesResponseType(typeof(ScheduleCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> AddSchedule(
        Guid id,
        CreateScheduleRequest request,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (route, actor, error) = await LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (
            string.IsNullOrWhiteSpace(request.RRule)
            || !request.RRule.Contains("FREQ=", StringComparison.OrdinalIgnoreCase)
        )
            return Results.BadRequest(new { error = "a schedule needs an RRULE with FREQ" });
        var schedule = new PatrolSchedule
        {
            Id = Guid.CreateVersion7(),
            OrgId = route!.OrgId,
            RouteId = route.Id,
            SiteId = route.SiteId,
            RRule = request.RRule.Trim(),
            AnchorDate = request.AnchorDate,
            StartLocal = request.StartLocal,
            ExDates = request.ExDates ?? [],
        };
        db.Schedules.Add(schedule);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new ScheduleCreated(schedule.Id));
    }

    [Transactional(typeof(PatrolsDbContext))]
    [WolverineDelete("/api/patrols/schedules/{id}")]
    [ProducesResponseType(typeof(ScheduleRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> RemoveSchedule(
        Guid id,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var schedule = await db.Schedules.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null)
            return Results.NotFound();
        var (_, _, error) = await LoadForManage(schedule.RouteId, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        db.Schedules.Remove(schedule);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new ScheduleRemoved(id));
    }

    internal static async Task<(PatrolRoute?, ActorRef?, IResult?)> LoadForManage(
        Guid id,
        PatrolsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return (null, null, Results.Unauthorized());
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.PatrolsManage, ct);
        if (scope is NodeScope.None)
            return (null, null, Results.Unauthorized());
        var route = await db.Routes.InScope(scope).FirstOrDefaultAsync(r => r.Id == id, ct);
        return route is null ? (null, null, Results.NotFound()) : (route, actor, null);
    }

    internal static Checkpoint[] Checkpoints(PatrolRoute route) =>
        JsonSerializer.Deserialize<Checkpoint[]>(route.CheckpointsJson) ?? [];

    internal static RouteView View(PatrolRoute route, IEnumerable<PatrolSchedule> schedules) =>
        new(
            route.Id,
            route.SiteId,
            route.Name,
            Checkpoints(route),
            route.ExpectedMinutes,
            route.Archived,
            schedules
                .Where(s => s.RouteId == route.Id)
                .Select(s => new ScheduleView(
                    s.Id,
                    s.RRule,
                    s.AnchorDate,
                    s.StartLocal,
                    s.ExDates,
                    s.Active
                ))
                .ToList()
        );

    private static string? Validate(string name, Checkpoint[]? checkpoints, int? minutes)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            return "a route needs a name of up to 200 characters";
        if (checkpoints is null || checkpoints.Length == 0 || checkpoints.Length > 100)
            return "a route needs 1 to 100 checkpoints";
        if (checkpoints.Any(c => string.IsNullOrWhiteSpace(c.Code) || c.Code.Trim().Length > 40))
            return "every checkpoint needs a code of up to 40 characters";
        if (
            checkpoints.Select(c => c.Code.Trim().ToUpperInvariant()).Distinct().Count()
            != checkpoints.Length
        )
            return "checkpoint codes must be unique";
        if (checkpoints.Any(c => (c.Latitude is null) != (c.Longitude is null)))
            return "checkpoint coordinates come as a pair";
        if (minutes is < 1 or > 1440)
            return "expected minutes must be 1-1440";
        return null;
    }

    private static Checkpoint[] Clean(Checkpoint[] checkpoints) =>
        checkpoints
            .Select(c => new Checkpoint(
                c.Code.Trim().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(c.Label)
                    ? c.Code.Trim().ToUpperInvariant()
                    : c.Label.Trim(),
                c.Latitude,
                c.Longitude
            ))
            .ToArray();
}
