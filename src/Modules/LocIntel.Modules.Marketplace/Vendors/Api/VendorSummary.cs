namespace LocIntel.Modules.Marketplace.Vendors.Api;

public sealed record VendorSummary(
    Guid OrgId,
    string Name,
    string Description,
    string[] Categories,
    string[] ServiceAreas,
    int ValidCredentials,
    int ExpiredCredentials,
    bool Preferred,
    bool Blocked
);
