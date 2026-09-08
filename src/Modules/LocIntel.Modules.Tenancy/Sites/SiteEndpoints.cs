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
            return ApiErrors.BadRequest($"'{request.TimeZone}' is not an IANA time zone");
        var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == request.NodeId, ct);
        if (node is null)
            return Results.NotFound();

        // Gates 2+3 on the write side: the grant must COVER the target node.
        var writeScope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesManage, ct);
        if (!writeScope.Covers(node.Path.ToString()))
            return Results.Forbid();

        // Gate 1 (ADR 8/9): a limit failure is 402-and-upsell, never an error.
        if (
            !await CapacityReservations.TryLockAsync(
                db,
                node.OrgId,
                EntitlementCatalog.MaxSites,
                ct
            )
        )
            return ApiErrors.Status(
                "site capacity is busy; retry the request",
                StatusCodes.Status503ServiceUnavailable
            );
        var siteCount =
            await db.Sites.LongCountAsync(ct)
            + await CapacityReservations.PendingAsync(
                db,
                node.OrgId,
                EntitlementCatalog.MaxSites,
                ct
            );
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

    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
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
            return ApiErrors.Conflict("this site was changed by someone else; reload and retry");

        var timeZoneChanged = false;
        if (request.TimeZone is { } zone && zone != site.TimeZone)
        {
            if (!BusinessDate.IsValidTimeZone(zone))
                return ApiErrors.BadRequest($"'{zone}' is not an IANA time zone");
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
                return ApiErrors.BadRequest("coordinates out of range");
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

    /// <summary>
    /// One status for many sites (the list's selection bar): each site the
    /// grant covers changes, the rest are counted and left alone - scope
    /// filters, it never errors. Node moves are not offered; a site's path is
    /// stamped into its facts, so relocating one is its own flow.
    /// </summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/sites/bulk-status")]
    [ProducesResponseType(typeof(BulkSiteStatusResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> BulkStatus(
        BulkSiteStatusRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (request.Ids.Length is 0 or > 500)
            return ApiErrors.BadRequest("choose between 1 and 500 sites");
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.SitesManage, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var ids = request.Ids.Distinct().Select(id => new SiteId(id)).ToArray();
        var sites = await db.Sites.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        var updated = 0;
        foreach (var site in sites)
        {
            if (!scope.Covers(site.Path.ToString()))
                continue;
            if (site.Status != request.Status)
                site.Status = request.Status;
            updated++;
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new BulkSiteStatusResponse(updated, ids.Length - updated));
    }

    internal static SiteResponse ToResponse(Site s) =>
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
