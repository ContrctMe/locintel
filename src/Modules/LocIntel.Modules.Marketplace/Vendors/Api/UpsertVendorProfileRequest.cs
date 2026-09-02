using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Vendors.Api;

public sealed record UpsertVendorProfileRequest(
    string Name,
    string? Description,
    ServiceCategory[] Categories,
    string[]? ServiceAreas,
    double? Latitude,
    double? Longitude,
    double? ServiceRadiusKm,
    string? ContactEmail,
    string? ContactPhone
);
