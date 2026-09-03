using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Vendors.Messages;

/// <summary>The vendor's public projection, published on every change while the profile is Published.</summary>
public sealed record VendorListingPublished(
    OrgId OrgId,
    string Name,
    string Description,
    string[] Categories,
    string[] ServiceAreas,
    double? Latitude,
    double? Longitude,
    double? ServiceRadiusKm,
    string? ContactEmail,
    string? ContactPhone,
    string CredentialsJson,
    DateTimeOffset UpdatedAt
);
