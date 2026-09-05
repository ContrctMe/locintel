using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Hierarchy;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Sites;

public sealed record CreateSiteRequest(
    Guid NodeId,
    string Name,
    string TimeZone,
    string? AddressLine1 = null,
    string? City = null,
    string? PostalCode = null,
    string? CountryCode = null,
    double? Latitude = null,
    double? Longitude = null
);

/// <summary>
/// Patch semantics: null = unchanged. Address fields accept "" to CLEAR -
/// an address typo must be fixable, and so must an address that never
/// existed (finding 2 of the competitive review).
/// </summary>
public sealed record UpdateSiteRequest(
    string? Name,
    string? TimeZone,
    SiteStatus? Status,
    string? AddressLine1 = null,
    string? City = null,
    string? PostalCode = null,
    string? CountryCode = null,
    double? Latitude = null,
    double? Longitude = null,
    System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>? Attributes = null,
    uint? Version = null
);

public sealed record CreateScheduleRequest(
    string Name,
    string RRule,
    DateOnly AnchorDate,
    TimeOnly Opens,
    TimeOnly Closes,
    DateOnly[]? ExDates = null
);

public sealed record SiteResponse(
    Guid Id,
    Guid NodeId,
    string Name,
    string TimeZone,
    string Status,
    string Path,
    uint Version,
    string? AddressLine1,
    string? City,
    string? PostalCode,
    string? CountryCode,
    double? Latitude,
    double? Longitude,
    System.Text.Json.JsonElement Attributes
);

/// <summary>
/// Site queries take a REQUIRED NodeScope (the third gate): resolved once per
/// request from the principal, applied as an ltree predicate. No endpoint
/// hand-writes an org or location filter.
/// </summary>
public static class SiteEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/sites")]
    [ProducesResponseType(typeof(SiteResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateSiteRequest request,
        TenancyDbContext db,
        IMessageBus bus,
        IEntitlements entitlements,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!BusinessDate.IsValidTimeZone(request.TimeZone))
            return Results.BadRequest(
                new { error = $"'{request.TimeZone}' is not an IANA time zone" }
            );
        var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == request.NodeId, ct);
        if (node is null)
            return Results.NotFound();

        // Gates 2+3 on the write side: the grant must COVER the target node.
        var writeScope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesManage, ct);
        if (!writeScope.Covers(node.Path.ToString()))
            return Results.Forbid();

        // Gate 1 (ADR 8/9): a limit failure is 402-and-upsell, never an error.
        var siteCount = await db.Sites.LongCountAsync(ct);
        var decision = await entitlements.CheckLimitAsync(
            node.OrgId,
            EntitlementCatalog.MaxSites,
            siteCount,
            1,
            ct
        );
        if (!decision.IsAllowed)
            return GateResults.LimitReached(decision);

        var id = SiteId.New();
        var site = new Site
        {
            Id = id,
            OrgId = node.OrgId,
            NodeId = node.Id,
            Name = request.Name,
            TimeZone = request.TimeZone,
            Path = new Microsoft.EntityFrameworkCore.LTree($"{node.Path}.{Site.Label(id)}"),
            AddressLine1 = request.AddressLine1,
            City = request.City,
            PostalCode = request.PostalCode,
            CountryCode = request.CountryCode,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(site));
    }

    [Transactional(typeof(TenancyDbContext))]
    /// <summary>
    /// Fleet-scale list (UX gap: server paging/search): filtered by scope
    /// FIRST, then searched, then paged. Offset paging on purpose - it
    /// translates everywhere and is right at template scale; keyset is a
    /// fork optimization past ~100k rows.
    /// </summary>
    [WolverineGet("/api/sites")]
    public static async Task<SiteListResponse> List(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        Guid? under,
        string? q,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        var query = db.Sites.InScope(scope);
        if (under is { } nodeId)
        {
            var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == nodeId, ct);
            if (node is null)
                return new SiteListResponse([], 0, 0, null);
            var nodePath = node.Path;
            query = query.Where(s => s.Path.IsDescendantOf(nodePath));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.Name, pattern)
                || (s.City != null && EF.Functions.ILike(s.City, pattern))
            );
        }
        var total = await query.CountAsync(ct);
        var openCount = await query.CountAsync(s => s.Status == SiteStatus.Open, ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var sites = await query.OrderBy(s => s.Name).Skip(skip).Take(take).ToListAsync(ct);
        return new SiteListResponse(
            sites.Select(ToResponse).ToList(),
            total,
            openCount,
            skip + sites.Count < total ? skip + sites.Count : null
        );
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/sites/open-now")]
    public static async Task<IReadOnlyList<SiteResponse>> OpenNow(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        var now = time.GetUtcNow();
        // The projection (ADR 28) makes this an indexed range query, not an
        // in-process RRULE expansion over every site.
        return
            await db
                .Sites.InScope(scope)
                .Where(s =>
                    db.SiteOpenWindows.Any(w =>
                        w.SiteId == s.Id && w.StartsAtUtc <= now && now < w.EndsAtUtc
                    )
                )
                .OrderBy(s => s.Name)
                .ToListAsync(ct)
                is { } open
            ? open.Select(ToResponse).ToList()
            : [];
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/sites/{id}")]
    [ProducesResponseType(typeof(SiteResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        // the scope gate applies to id-addressed reads too: outside the
        // grant's subtree is 404, same as outside the tenant
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        return site is null || !scope.Covers(site.Path.ToString())
            ? Results.NotFound()
            : Results.Ok(ToResponse(site));
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/sites/{id}")]
    [ProducesResponseType(typeof(SiteResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Update(
        Guid id,
        UpdateSiteRequest request,
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
        var updateScope = await scopes.ScopeForAsync(
            accessor.Current,
            Capabilities.SitesManage,
            ct
        );
        if (!updateScope.Covers(site.Path.ToString()))
            return Results.Forbid();
        // optimistic concurrency: the client echoes the version it edited;
        // a mismatch means someone else saved first - 409, never a clobber
        if (request.Version is { } version && version != site.Version)
            return Results.Conflict(
                new { error = "this site was changed by someone else; reload and retry" }
            );

        var timeZoneChanged = false;
        if (request.TimeZone is { } zone && zone != site.TimeZone)
        {
            if (!BusinessDate.IsValidTimeZone(zone))
                return Results.BadRequest(new { error = $"'{zone}' is not an IANA time zone" });
            site.TimeZone = zone;
            timeZoneChanged = true;
        }
        if (request.Name is { } name)
            site.Name = name;
        if (request.Status is { } status)
            site.Status = status;
        if (request.AddressLine1 is { } line1)
            site.AddressLine1 = line1.Length == 0 ? null : line1;
        if (request.City is { } city)
            site.City = city.Length == 0 ? null : city;
        if (request.PostalCode is { } postal)
            site.PostalCode = postal.Length == 0 ? null : postal;
        if (request.CountryCode is { } country)
            site.CountryCode = country.Length == 0 ? null : country.ToUpperInvariant();
        if (request is { Latitude: { } latitude, Longitude: { } longitude })
        {
            if (Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
                return Results.BadRequest(new { error = "coordinates out of range" });
            site.Latitude = latitude;
            site.Longitude = longitude;
        }
        if (request.Attributes is { Count: > 0 } incoming)
        {
            var (error, merged) = await SiteAttributeEndpoints.MergeAttributesAsync(
                db,
                site.AttributesJson,
                incoming,
                ct
            );
            if (error is not null)
                return Results.BadRequest(new { error });
            site.AttributesJson = merged!;
        }
        await db.SaveChangesAsync(ct);

        // The rebuild trigger everyone forgets (ADR 28): a timezone change
        // shifts every published open window.
        if (timeZoneChanged)
            await bus.PublishForOrgAsync(site.OrgId, new RebuildSiteOccurrences(site.Id.Value));
        return Results.Ok(ToResponse(site));
    }

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
            return Results.BadRequest(new { error = "invalid RRULE" });

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

    public sealed record SiteListResponse(
        IReadOnlyList<SiteResponse> Items,
        int Total,
        int OpenCount,
        int? NextOffset
    );

    private static SiteResponse ToResponse(Site s) =>
        new(
            s.Id.Value,
            s.NodeId,
            s.Name,
            s.TimeZone,
            s.Status.ToString(),
            s.Path.ToString(),
            s.Version,
            s.AddressLine1,
            s.City,
            s.PostalCode,
            s.CountryCode,
            s.Latitude,
            s.Longitude,
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                s.AttributesJson
            )
        );
}

public sealed record ScheduleResponse(
    Guid Id,
    string Name,
    string RRule,
    DateOnly AnchorDate,
    TimeOnly Opens,
    TimeOnly Closes,
    DateOnly[] ExDates
);

public sealed record ScheduleCreatedResponse(Guid Id);

public sealed record SiteWindowResponse(
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateOnly LocalDate
);

/// <summary>Schedule listing/removal and the projection preview - the hours editor's backend.</summary>
public static class ScheduleEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
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
    [Transactional(typeof(TenancyDbContext))]
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
