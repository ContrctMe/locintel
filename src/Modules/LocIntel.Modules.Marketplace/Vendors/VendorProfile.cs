using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Vendors;

/// <summary>
/// A vendor is an ORGANIZATION (ADR 5: its people are ordinary members) that
/// keeps a profile. Owned by the vendor org and read only there (ADR 48);
/// publishing projects it into the platform-global vendor directory that
/// buyers search, and unpublishing withdraws it. Deletion: unpublish (tier
/// 1); never deleted.
/// </summary>
public sealed class VendorProfile : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public string[] Categories { get; set; } = [];

    /// <summary>Where they serve: region/state codes, free-form, matched case-insensitively.</summary>
    public string[] ServiceAreas { get; set; } = [];
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? ServiceRadiusKm { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public bool Published { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
