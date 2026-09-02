namespace LocIntel.Modules.Marketplace.Vendors.Api;

public sealed record VendorProfileView(
    Guid OrgId,
    string Name,
    string Description,
    string[] Categories,
    string[] ServiceAreas,
    double? Latitude,
    double? Longitude,
    double? ServiceRadiusKm,
    string? ContactEmail,
    string? ContactPhone,
    bool Published,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CredentialView> Credentials
);
