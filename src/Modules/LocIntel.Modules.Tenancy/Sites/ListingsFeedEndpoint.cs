using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Sites;

public sealed record ListingHours(
    string Name,
    string RRule,
    DateOnly AnchorDate,
    string Opens,
    string Closes,
    DateOnly[] ClosedDates
);

public sealed record ListingRecord(
    Guid Id,
    string Name,
    string Status,
    string TimeZone,
    string? AddressLine1,
    string? City,
    string? PostalCode,
    string? CountryCode,
    double? Latitude,
    double? Longitude,
    string PublicUrl,
    IReadOnlyList<ListingHours> Hours,
    System.Text.Json.JsonElement Attributes
);

public sealed record ListingsFeedResponse(
    DateTimeOffset GeneratedAt,
    string Organization,
    IReadOnlyList<ListingRecord> Listings
);

/// <summary>
/// The canonical listings export (ADR 44): every site as a full listing
/// record - identity, address, coordinates, status, and the hours RULES
/// (RRULE + exception dates), which is what listings providers want; they
/// expand recurrence themselves. Built for connector consumption: poll this
/// with an API key holding sites:read (ADR 40), subscribe to site.* webhooks
/// to know when. Scope filters as everywhere else - a subtree-scoped key
/// exports its subtree.
/// </summary>
public static class ListingsFeedEndpoint
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/listings/feed")]
    [ProducesResponseType(typeof(ListingsFeedResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Feed(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IConfiguration configuration,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        OrgId orgId;
        switch (scope)
        {
            case NodeScope.EntireOrg entire:
                orgId = entire.Org;
                break;
            case NodeScope.Subtrees subtrees:
                orgId = subtrees.Org;
                break;
            default:
                return Results.Unauthorized();
        }
        // the org row explicitly by id: the anchor table is platform-global,
        // an unfiltered First() would be a cross-tenant read
        var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);
        var template = configuration["Public:HostTemplate"] ?? "http://{slug}.localhost:5174";
        var publicBase = template.Replace("{slug}", org.Slug);

        var sites = (await db.Sites.OrderBy(s => s.Name).ToListAsync(ct))
            .Where(s => scope.Covers(s.Path.ToString()))
            .ToList();
        var siteIds = sites.Select(s => s.Id).ToList();
        var schedules = await db
            .SiteSchedules.Where(sc => siteIds.Contains(sc.SiteId))
            .OrderBy(sc => sc.Name)
            .ToListAsync(ct);
        var schedulesBySite = schedules.ToLookup(sc => sc.SiteId);

        var listings = sites
            .Select(s => new ListingRecord(
                s.Id.Value,
                s.Name,
                s.Status.ToString(),
                s.TimeZone,
                s.AddressLine1,
                s.City,
                s.PostalCode,
                s.CountryCode,
                s.Latitude,
                s.Longitude,
                $"{publicBase}/sites/{s.Id.Value}",
                schedulesBySite[s.Id]
                    .Select(sc => new ListingHours(
                        sc.Name,
                        sc.RRule,
                        sc.AnchorDate,
                        sc.OpensLocal.ToString("HH:mm"),
                        sc.ClosesLocal.ToString("HH:mm"),
                        sc.ExDates
                    ))
                    .ToList(),
                System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                    s.AttributesJson
                )
            ))
            .ToList();
        return Results.Ok(new ListingsFeedResponse(DateTimeOffset.UtcNow, org.Name, listings));
    }
}
