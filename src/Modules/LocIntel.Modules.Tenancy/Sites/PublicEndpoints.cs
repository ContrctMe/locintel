using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
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
    [Transactional(typeof(TenancyDbContext))]
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

    /// <summary>
    /// The locator list (ADR 43): geo-aware when asked. ?near=lat,lng sorts
    /// by great-circle distance and returns distanceKm; sites without
    /// coordinates sort last, alphabetical. Distance math runs in memory on
    /// purpose - the public fleet list is unpaged and modest by design, and
    /// haversine-in-SQL buys translation risk for nothing at this size.
    /// </summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/public/sites")]
    [ProducesResponseType(typeof(List<PublicSiteSummary>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        string? near,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.PublicRead, ct))
            return Results.Ok(Array.Empty<object>()); // unknown host: empty, never an error
        var now = time.GetUtcNow();
        var sites = await db
            .Sites.Where(s => s.Status == SiteStatus.Open || s.Status == SiteStatus.ComingSoon)
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                id = s.Id.Value,
                s.Name,
                s.City,
                s.TimeZone,
                s.Latitude,
                s.Longitude,
                status = s.Status.ToString(),
                openNow = db.SiteOpenWindows.Any(w =>
                    w.SiteId == s.Id && w.StartsAtUtc <= now && now < w.EndsAtUtc
                ),
            })
            .ToListAsync(ct);

        (double Lat, double Lng)? origin = null;
        if (near?.Split(',') is [var latRaw, var lngRaw])
        {
            var style = System.Globalization.CultureInfo.InvariantCulture;
            if (
                double.TryParse(latRaw, style, out var lat)
                && double.TryParse(lngRaw, style, out var lng)
                && Math.Abs(lat) <= 90
                && Math.Abs(lng) <= 180
            )
                origin = (lat, lng);
        }

        var shaped = sites
            .Select(s => new PublicSiteSummary(
                s.id,
                s.Name,
                s.City,
                s.TimeZone,
                s.Latitude,
                s.Longitude,
                s.status,
                s.openNow,
                origin is { } from && s.Latitude is { } slat && s.Longitude is { } slng
                    ? Math.Round(HaversineKm(from.Lat, from.Lng, slat, slng), 1)
                    : null
            ))
            .OrderBy(s => s.DistanceKm ?? double.MaxValue)
            .ThenBy(s => s.Name)
            .ToList();
        return Results.Ok(shaped);
    }

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusKm = 6371.0;
        static double Rad(double degrees) => degrees * Math.PI / 180.0;
        var halfLat = Math.Sin(Rad(lat2 - lat1) / 2);
        var halfLng = Math.Sin(Rad(lng2 - lng1) / 2);
        var a = halfLat * halfLat + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * halfLng * halfLng;
        return 2 * earthRadiusKm * Math.Asin(Math.Sqrt(a));
    }

    [Transactional(typeof(TenancyDbContext))]
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
