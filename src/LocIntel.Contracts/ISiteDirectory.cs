namespace LocIntel.Contracts;

/// <summary>
/// What another module may know about a site (ADR 37 direction: Tenancy
/// implements, modules above consume).
///
/// It carries the hierarchy id and the location fields from day one because
/// ADR 2/4 require fact tables to stamp the ancestor path KEYED BY hierarchy
/// id - a consumer holding only the path cannot satisfy that - and because
/// every site-scoped feature that reaches for a site eventually wants where
/// it is. A fork extended this record three separate times; each extension
/// is a breaking change to a published contract, so the cheap fields ship up
/// front rather than one migration at a time.
/// </summary>
public sealed record SiteInfo(
    Guid Id,
    string Name,
    string Path,
    string TimeZone,
    Guid HierarchyId,
    double? Latitude = null,
    double? Longitude = null,
    string? City = null,
    string? PostalCode = null,
    string? CountryCode = null
);

/// <summary>
/// Site lookup for modules that hang site-scoped features off Tenancy's
/// sites without touching its tables - the scope path (for gate 3 checks)
/// and the IANA zone (for site-local business dates, ADR 26).
/// </summary>
public interface ISiteDirectory
{
    Task<SiteInfo?> FindAsync(Guid siteId, CancellationToken ct = default);
}
