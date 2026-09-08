using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Spatial;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Sites;

public sealed record PublicOrgResponse(string Name, string Slug, string? BrandColor);

/// <summary>
/// The guest surface (ADR 7): public-safe site data for the host-derived org,
/// behind the SAME gates as everything else - guests hold exactly public:read
/// over their org, and RLS scopes the rows. Closed sites and internal fields
/// (paths, nodes, external ids) never appear here.
/// </summary>
public sealed record PublicSiteSummary(
    Guid Id,
    string Name,
    string? City,
    string TimeZone,
    double? Lat,
    double? Lng,
    string Status,
    bool OpenNow,
    double? DistanceKm
);

public sealed record PublicOpenWindow(
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateOnly LocalDate
);

/// <summary>A page of the locator: the sites and the cursor for the next page (null on a nearest-first page and at the end).</summary>
public sealed record PublicSiteListResponse(IReadOnlyList<PublicSiteSummary> Items, string? Next);

public sealed record PublicSiteAttribute(
    string Key,
    string Label,
    System.Text.Json.JsonElement Value
);

public sealed record PublicSiteDetailResponse(
    Guid Id,
    string Name,
    string? City,
    string? AddressLine1,
    string? PostalCode,
    string? CountryCode,
    string TimeZone,
    string Status,
    bool OpenNow,
    IReadOnlyList<PublicOpenWindow> Windows,
    IReadOnlyList<string> Closures,
    IReadOnlyList<PublicSiteAttribute> Attributes
);

public static class PublicSiteEndpoints
{
    /// <summary>
    /// Whose page is this? The org's public identity - name for the title and
    /// header, brand color as the fork-ready theming hook (the seeded
    /// brand.color org setting finally has a reader). A shell enhancer: 404
    /// when the host resolves to nothing, and the page renders unbranded.
    /// </summary>
    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/public/org")]
    [ProducesResponseType(typeof(PublicOrgResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> OrgIdentity(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.PublicRead, ct))
            return Results.NotFound();
        OrgId? principalOrg = accessor.Current switch
        {
            Principal.Guest { Org: { } guestOrg } => guestOrg,
            Principal.Contact contact => contact.Org,
            Principal.User { ActiveOrg: { } activeOrg } => activeOrg,
            _ => null,
        };
        if (principalOrg is not { } org)
            return Results.NotFound();
        var organization = await db
            .Organizations.Where(o => o.Id == org)
            .Select(o => new { o.Name, o.Slug })
            .FirstOrDefaultAsync(ct);
        if (organization is null)
            return Results.NotFound();
        var brandColor = await db
            .OrganizationSettings.Where(s => s.Key == "brand.color")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return Results.Ok(new PublicOrgResponse(organization.Name, organization.Slug, brandColor));
    }

    /// <summary>The nearest-first list stops at these radii (km), widening until a page is full.</summary>
    private static readonly double[] RingsKm = [2, 10, 50, 250, 1000];

    /// <summary>The most candidates one ring reads; a denser ring than this is the city centre, and the first ring already holds a page.</summary>
    private const int RingCap = 2_000;

    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    /// <summary>
    /// The locator list (ADR 43), paged (code review, 2026-09): alphabetical
    /// pages ride the (name, id) keyset like the console's list; ?near=lat,lng
    /// returns the nearest page instead, found by widening rings of the
    /// spatial cell key (ADR 51) so a million-site org never sorts itself in
    /// memory, then sites without coordinates, alphabetical, so a fleet that
    /// is only partly mapped still lists every location. Nearest pages carry
    /// no cursor: the point of "near" is the closest ones.
    /// </summary>
    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/public/sites")]
    [ProducesResponseType(typeof(PublicSiteListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        string? near,
        int? limit,
        string? after,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.PublicRead, ct))
            return Results.Ok(new PublicSiteListResponse([], null)); // unknown host: empty, never an error
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var now = time.GetUtcNow();
        var listed = db.Sites.Where(s =>
            s.Status == SiteStatus.Open || s.Status == SiteStatus.ComingSoon
        );

        if (Origin(near) is { } from)
            return Results.Ok(
                new PublicSiteListResponse(
                    await NearestAsync(db, listed, from, take, now, ct),
                    null
                )
            );

        SiteCursor? cursor = null;
        if (after is not null)
        {
            if (!SiteCursor.TryParse(after, out var parsed))
                return ApiErrors.BadRequest("after is not a cursor this list issued");
            cursor = parsed;
        }
        var page = listed;
        if (cursor is { Name: var afterName, Id: var afterId })
            page = page.Where(s =>
                s.Name.CompareTo(afterName) >= 0
                && (s.Name.CompareTo(afterName) > 0 || s.Id.CompareTo(afterId) > 0)
            );
        var sites = await page.OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Take(take + 1)
            .ToListAsync(ct);
        var more = sites.Count > take;
        if (more)
            sites.RemoveAt(take);
        var open = await OpenNowAsync(db, sites, now, ct);
        return Results.Ok(
            new PublicSiteListResponse(
                sites.Select(s => Shape(s, open.Contains(s.Id), null)).ToList(),
                more ? SiteCursor.Encode(sites[^1]) : null
            )
        );
    }

    private static (double Lat, double Lng)? Origin(string? near)
    {
        if (near?.Split(',') is not [var latRaw, var lngRaw])
            return null;
        var style = System.Globalization.CultureInfo.InvariantCulture;
        return
            double.TryParse(latRaw, style, out var lat)
            && double.TryParse(lngRaw, style, out var lng)
            && Math.Abs(lat) <= 90
            && Math.Abs(lng) <= 180
            ? (lat, lng)
            : null;
    }

    private static async Task<List<PublicSiteSummary>> NearestAsync(
        TenancyDbContext db,
        IQueryable<Site> listed,
        (double Lat, double Lng) from,
        int take,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        List<Site> candidates = [];
        foreach (var radius in RingsKm)
        {
            candidates = await listed
                .Where(SpatialPredicates.InViewport<Site>(BoxAround(from, radius)))
                .OrderBy(s => s.Cell) // the (org_id, cell) index's own order: a stable cap, no sort
                .Take(RingCap)
                .ToListAsync(ct);
            if (candidates.Count >= take)
                break;
        }
        var nearest = candidates
            .Select(s =>
                (
                    site: s,
                    km: HaversineKm(from.Lat, from.Lng, s.Latitude!.Value, s.Longitude!.Value)
                )
            )
            .OrderBy(x => x.km)
            .ThenBy(x => x.site.Name)
            .Take(take)
            .ToList();
        var sites = nearest.Select(x => x.site).ToList();
        // a fleet that is only partly mapped still lists every location
        if (sites.Count < take)
            sites.AddRange(
                await listed
                    .Where(s => s.Cell == null)
                    .OrderBy(s => s.Name)
                    .ThenBy(s => s.Id)
                    .Take(take - sites.Count)
                    .ToListAsync(ct)
            );
        var open = await OpenNowAsync(db, sites, now, ct);
        var distances = nearest.ToDictionary(x => x.site.Id, x => Math.Round(x.km, 1));
        return sites
            .Select(s =>
                Shape(s, open.Contains(s.Id), distances.TryGetValue(s.Id, out var km) ? km : null)
            )
            .ToList();
    }

    /// <summary>A box <paramref name="km"/> around the origin, clamped to the Mercator world and wrapped at the antimeridian.</summary>
    private static BoundingBox BoxAround((double Lat, double Lng) origin, double km)
    {
        const double kmPerDegree = 111.32;
        var dLat = km / kmPerDegree;
        var south = Math.Max(-SpatialCells.MaxLatitude, origin.Lat - dLat);
        var north = Math.Min(SpatialCells.MaxLatitude, origin.Lat + dLat);
        var cos = Math.Cos(origin.Lat * Math.PI / 180);
        var dLng = cos < 0.01 ? 180 : km / (kmPerDegree * cos);
        if (dLng >= 180)
            return new BoundingBox(-180, south, 180, north);
        var west = origin.Lng - dLng;
        var east = origin.Lng + dLng;
        if (west < -180)
            west += 360;
        if (east > 180)
            east -= 360;
        return new BoundingBox(west, south, east, north);
    }

    /// <summary>One read for the page's "open now" flags: the windows that contain this instant for these sites.</summary>
    private static async Task<HashSet<SiteId>> OpenNowAsync(
        TenancyDbContext db,
        IReadOnlyList<Site> sites,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        if (sites.Count == 0)
            return [];
        var ids = sites.Select(s => s.Id).ToArray();
        return (
            await db
                .SiteOpenWindows.Where(w =>
                    ids.Contains(w.SiteId) && w.StartsAtUtc <= now && now < w.EndsAtUtc
                )
                .Select(w => w.SiteId)
                .Distinct()
                .ToListAsync(ct)
        ).ToHashSet();
    }

    private static PublicSiteSummary Shape(Site s, bool openNow, double? distanceKm) =>
        new(
            s.Id.Value,
            s.Name,
            s.City,
            s.TimeZone,
            s.Latitude,
            s.Longitude,
            s.Status.ToString(),
            openNow,
            distanceKm
        );

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusKm = 6371.0;
        static double Rad(double degrees) => degrees * Math.PI / 180.0;
        var halfLat = Math.Sin(Rad(lat2 - lat1) / 2);
        var halfLng = Math.Sin(Rad(lng2 - lng1) / 2);
        var a = halfLat * halfLat + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * halfLng * halfLng;
        return 2 * earthRadiusKm * Math.Asin(Math.Sqrt(a));
    }

    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/public/sites/{id}")]
    [ProducesResponseType(typeof(PublicSiteDetailResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.PublicRead, ct))
            return Results.NotFound();
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(
            s =>
                s.Id == siteId
                && (s.Status == SiteStatus.Open || s.Status == SiteStatus.ComingSoon),
            ct
        );
        if (site is null)
            return Results.NotFound();

        var now = time.GetUtcNow();
        var horizon = now.AddDays(7);
        var windows = await db
            .SiteOpenWindows.Where(w =>
                w.SiteId == siteId && w.EndsAtUtc > now && w.StartsAtUtc < horizon
            )
            .OrderBy(w => w.StartsAtUtc)
            .Select(w => new
            {
                w.StartsAtUtc,
                w.EndsAtUtc,
                w.LocalDate,
            })
            .ToListAsync(ct);
        // upcoming holiday closures (next 30 site-local days), so the page
        // can say "Closed Dec 25" instead of silently skipping the day
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo
                .ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(site.TimeZone))
                .DateTime
        );
        var closureHorizon = localToday.AddDays(30);
        var closures = (
            await db
                .SiteSchedules.Where(s => s.SiteId == siteId)
                .Select(s => s.ExDates)
                .ToListAsync(ct)
        )
            .SelectMany(dates => dates)
            .Where(date => date >= localToday && date <= closureHorizon)
            .Distinct()
            .OrderBy(date => date)
            .Select(date => date.ToString("yyyy-MM-dd"))
            .ToList();
        // PUBLIC attribute values only (ADR 46): the definition's flag is the
        // visibility gate, labels ride along for display
        var publicDefinitions = await db
            .SiteAttributeDefinitions.Where(d => d.Public)
            .OrderBy(d => d.Key)
            .ToListAsync(ct);
        var values = System.Text.Json.JsonSerializer.Deserialize<
            Dictionary<string, System.Text.Json.JsonElement>
        >(site.AttributesJson);
        var attributes = publicDefinitions
            .Where(d => values is not null && values.ContainsKey(d.Key))
            .Select(d => new PublicSiteAttribute(d.Key, d.Label, values![d.Key]))
            .ToList();
        return Results.Ok(
            new PublicSiteDetailResponse(
                site.Id.Value,
                site.Name,
                site.City,
                site.AddressLine1,
                site.PostalCode,
                site.CountryCode,
                site.TimeZone,
                site.Status.ToString(),
                windows.Any(w => w.StartsAtUtc <= now && now < w.EndsAtUtc),
                windows
                    .Select(w => new PublicOpenWindow(w.StartsAtUtc, w.EndsAtUtc, w.LocalDate))
                    .ToList(),
                closures,
                attributes
            )
        );
    }
}
