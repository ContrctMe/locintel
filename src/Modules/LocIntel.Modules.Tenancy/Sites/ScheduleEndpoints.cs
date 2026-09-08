using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Hierarchy;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Spatial;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>Schedule listing/removal and the projection preview - the hours editor's backend.</summary>
public static class ScheduleEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/sites/{id}/schedules")]
    [ProducesResponseType(typeof(ScheduleCreatedResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> CreateSchedule(
        Guid id,
        CreateScheduleRequest request,
        TenancyDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site is null)
            return Results.NotFound();
        var scheduleScope = await scopes.ScopeForAsync(
            accessor.Current,
            Capabilities.SitesManage,
            ct
        );
        if (!scheduleScope.Covers(site.Path.ToString()))
            return Results.Forbid();
        if (!LocIntel.Platform.Scheduling.RecurrenceExpander.IsValidRule(request.RRule))
            return ApiErrors.BadRequest("invalid RRULE");

        var schedule = SiteSchedule.Create(
            site.OrgId,
            site.Id,
            request.Name,
            request.RRule,
            request.AnchorDate,
            request.Opens,
            request.Closes
        );
        schedule.ExDates = request.ExDates ?? [];
        db.SiteSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);

        await bus.PublishForOrgAsync(site.OrgId, new RebuildSiteOccurrences(site.Id.Value));
        return Results.Ok(new ScheduleCreatedResponse(schedule.Id));
    }

    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/sites/{id}/schedules")]
    [ProducesResponseType(typeof(List<ScheduleResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        if (site is null || !scope.Covers(site.Path.ToString()))
            return Results.NotFound();
        var schedules = await db
            .SiteSchedules.Where(s => s.SiteId == siteId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
        return Results.Ok(
            schedules
                .Select(s => new ScheduleResponse(
                    s.Id,
                    s.Name,
                    s.RRule,
                    s.AnchorDate,
                    s.OpensLocal,
                    s.ClosesLocal,
                    s.ExDates
                ))
                .ToList()
        );
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverineDelete("/api/sites/{id}/schedules/{scheduleId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Delete(
        Guid id,
        Guid scheduleId,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site is null)
            return Results.NotFound();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesManage, ct);
        if (!scope.Covers(site.Path.ToString()))
            return Results.Forbid();
        var schedule = await db.SiteSchedules.FirstOrDefaultAsync(
            s => s.Id == scheduleId && s.SiteId == siteId,
            ct
        );
        if (schedule is null)
            return Results.NotFound();

        db.SiteSchedules.Remove(schedule);
        await db.SaveChangesAsync(ct);
        // a removed rule invalidates its windows (ADR 28 rebuild trigger)
        await bus.PublishForOrgAsync(site.OrgId, new RebuildSiteOccurrences(site.Id.Value));
        return Results.NoContent();
    }

    /// <summary>Upcoming open windows from the projection - "what these rules actually mean".</summary>
    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/sites/{id}/windows")]
    [ProducesResponseType(typeof(List<SiteWindowResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> Windows(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        int? days,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        if (site is null || !scope.Covers(site.Path.ToString()))
            return Results.NotFound();
        var now = time.GetUtcNow();
        var horizon = now.AddDays(Math.Clamp(days ?? 7, 1, 60));
        var windows = await db
            .SiteOpenWindows.Where(w =>
                w.SiteId == siteId && w.EndsAtUtc > now && w.StartsAtUtc < horizon
            )
            .OrderBy(w => w.StartsAtUtc)
            .Select(w => new SiteWindowResponse(w.StartsAtUtc, w.EndsAtUtc, w.LocalDate))
            .ToListAsync(ct);
        return Results.Ok(windows);
    }
}
