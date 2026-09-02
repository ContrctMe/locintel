namespace LocIntel.Modules.Marketplace.Vendors.Api;

public sealed record PreferredVendorView(
    Guid Id,
    Guid VendorOrgId,
    string? VendorName,
    string[] Categories,
    string? Notes,
    bool Blocked,
    DateTimeOffset CreatedAt
);
