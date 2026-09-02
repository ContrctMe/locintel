using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Sites;

public sealed record AddClosureRequest(DateOnly Date);

/// <summary>
/// Holiday closures (ADR 27's EXDATE, finally with a product): a closure is
/// SITE-level in the human's head - "closed Dec 25" - so these endpoints
/// write the date into every schedule's ExDates and rebuild the projection.
/// Dates are site-local business dates, the same kind the windows stamp.
/// </summary>
public static class ClosureEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/sites/{id}/closures")]
    public static async Task<IResult> List(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var (site, error) = await LoadCovered(id, db, accessor, scopes, Capabilities.SitesRead, ct);
        if (error is not null)
            return error;
        var schedules = await db
            .SiteSchedules.Where(s => s.SiteId == site!.Id)
            .Select(s => s.ExDates)
            .ToListAsync(ct);
        var today = TodayAt(site!, time);
        var closures = schedules
            .SelectMany(dates => dates)
            .Where(date => date >= today)
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        return Results.Ok(closures);
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/sites/{id}/closures")]
    public static async Task<IResult> Add(
        Guid id,
        AddClosureRequest request,
        TenancyDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var (site, error) = await LoadCovered(
            id,
            db,
            accessor,
            scopes,
            Capabilities.SitesManage,
            ct
        );
        if (error is not null)
            return error;
        if (request.Date < TodayAt(site!, time))
            return Results.BadRequest(new { error = "closures are for today or the future" });
        var schedules = await db.SiteSchedules.Where(s => s.SiteId == site!.Id).ToListAsync(ct);
        if (schedules.Count == 0)
            return Results.Conflict(new { error = "define hours before closing days" });

        foreach (var schedule in schedules)
            if (!schedule.ExDates.Contains(request.Date))
                schedule.ExDates = [.. schedule.ExDates, request.Date];
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(site!.OrgId, new RebuildSiteOccurrences(site.Id.Value));
        await PublishAudit(bus, accessor, site, "site.closure_added", request.Date);
        return Results.NoContent();
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverineDelete("/api/sites/{id}/closures/{date}")]
    public static async Task<IResult> Remove(
        Guid id,
        string date,
        TenancyDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!DateOnly.TryParse(date, out var day))
            return Results.BadRequest(new { error = "date must be yyyy-MM-dd" });
        var (site, error) = await LoadCovered(
            id,
            db,
            accessor,
            scopes,
            Capabilities.SitesManage,
            ct
        );
        if (error is not null)
            return error;
        var schedules = await db.SiteSchedules.Where(s => s.SiteId == site!.Id).ToListAsync(ct);
        var removed = false;
        foreach (var schedule in schedules)
            if (schedule.ExDates.Contains(day))
            {
                schedule.ExDates = [.. schedule.ExDates.Where(d => d != day)];
                removed = true;
            }
        if (!removed)
            return Results.NotFound();
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(site!.OrgId, new RebuildSiteOccurrences(site.Id.Value));
        await PublishAudit(bus, accessor, site, "site.closure_removed", day);
        return Results.NoContent();
    }

    /// <summary>"Today" on the SITE's clock - a viewer's timezone must not shift closure math.</summary>
    private static DateOnly TodayAt(Site site, TimeProvider time) =>
        DateOnly.FromDateTime(
            TimeZoneInfo
                .ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(site.TimeZone))
                .DateTime
        );

    private static async Task<(Site?, IResult?)> LoadCovered(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        string capability,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site is null)
            return (null, Results.NotFound());
        var scope = await scopes.ScopeForAsync(accessor.Current, capability, ct);
        if (!scope.Covers(site.Path.ToString()))
            return (
                null,
                capability == Capabilities.SitesRead ? Results.NotFound() : Results.Forbid()
            );
        return (site, null);
    }

    private static Task PublishAudit(
        IMessageBus bus,
        IPrincipalAccessor accessor,
        Site site,
        string eventName,
        DateOnly date
    )
    {
        var actor = accessor.Current as Principal.User;
        return bus.PublishAsync(
                new LocIntel.Contracts.RecordDomainAudit(
                    eventName,
                    System.Text.Json.JsonSerializer.Serialize(
                        new { siteId = site.Id.Value, date = date.ToString("yyyy-MM-dd") }
                    )
                ),
                new DeliveryOptions
                {
                    TenantId = site.OrgId.Value.ToString(),
                    Headers =
                    {
                        ["locintel-actor-tier"] = "user",
                        ["locintel-actor-id"] = actor?.UserId.ToString() ?? "",
                    },
                }
            )
            .AsTask();
    }
}
