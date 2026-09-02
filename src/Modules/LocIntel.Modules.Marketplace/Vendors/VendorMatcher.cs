using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Marketplace.Vendors;

/// <summary>
/// Who can serve a request: published, offers the category, not blocked by
/// the buyer, and REACHES the site - by service area (a vendor listing
/// areas must list the site's country) and by radius (a vendor with a
/// base and radius must have the site inside it; a vendor without either
/// serves anywhere). Preferred first, then nearest, then by name.
/// </summary>
public static class VendorMatcher
{
    public static async Task<List<OrgId>> MatchAsync(
        MarketplaceDbContext db,
        OrgId requester,
        ServiceCategory category,
        double? siteLatitude,
        double? siteLongitude,
        string? siteCountryCode,
        CancellationToken ct
    )
    {
        var name = category.ToString();
        var candidates = await db
            .Profiles.Where(p => p.Published && p.OrgId != requester && p.Categories.Contains(name))
            .Where(p => !db.Preferred.Any(x => x.VendorOrgId == p.OrgId && x.Blocked))
            .Select(p => new
            {
                p.OrgId,
                p.Name,
                p.ServiceAreas,
                p.Latitude,
                p.Longitude,
                p.ServiceRadiusKm,
                Preferred = db.Preferred.Any(x => x.VendorOrgId == p.OrgId && !x.Blocked),
            })
            .ToListAsync(ct);
        var country = siteCountryCode?.Trim().ToUpperInvariant();
        return candidates
            .Select(v => new
            {
                v.OrgId,
                v.Name,
                v.Preferred,
                Distance = v.Latitude is { } lat
                && v.Longitude is { } lng
                && siteLatitude is { } slat
                && siteLongitude is { } slng
                    ? Geo.DistanceMeters(lat, lng, slat, slng) / 1000
                    : (double?)null,
                Radius = v.ServiceRadiusKm,
                Areas = v.ServiceAreas,
            })
            .Where(v => v.Areas.Length == 0 || country is null || v.Areas.Contains(country))
            .Where(v => v.Radius is null || v.Distance is null || v.Distance <= v.Radius)
            .OrderByDescending(v => v.Preferred)
            .ThenBy(v => v.Distance ?? double.MaxValue)
            .ThenBy(v => v.Name)
            .Select(v => v.OrgId)
            .ToList();
    }
}
