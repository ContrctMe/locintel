using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Vendors.Api;

public sealed record SetPreferredVendorRequest(
    Guid VendorOrgId,
    ServiceCategory[]? Categories = null,
    string? Notes = null,
    bool Blocked = false
);
