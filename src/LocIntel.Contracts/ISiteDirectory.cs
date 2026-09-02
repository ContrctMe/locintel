namespace LocIntel.Contracts;

/// <summary>
/// What another module may know about a site (ADR 37 direction: Tenancy
/// implements, modules above consume). HierarchyId travels with the path so
/// fact tables can stamp a tree-keyed ancestor path (ADR 2/4).
/// </summary>
public sealed record SiteInfo(
    Guid Id,
    string Name,
    string Path,
    string TimeZone,
    Guid HierarchyId,
    double? Latitude = null,
    double? Longitude = null,
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
