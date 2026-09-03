using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Vendors;

/// <summary>
/// The platform-global vendor directory (ADR 48, "open: pull"): one row per
/// PUBLISHED vendor, fed by VendorListingPublished / VendorListingWithdrawn
/// from the vendor's own profile, exactly like identity.org_directory. No
/// RLS by design - every tenant reads it - so it carries only what anyone may
/// know (RlsCoverageTests.PlatformGlobal lists it with that reason).
/// </summary>
public sealed class VendorListing
{
    public required OrgId OrgId { get; init; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public string[] Categories { get; set; } = [];
    public string[] ServiceAreas { get; set; } = [];
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? ServiceRadiusKm { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }

    /// <summary>PublicCredential[] as jsonb; validity is judged at read time against the clock.</summary>
    public string CredentialsJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Offers(ServiceCategory category) => Categories.Contains(category.ToString());
}
