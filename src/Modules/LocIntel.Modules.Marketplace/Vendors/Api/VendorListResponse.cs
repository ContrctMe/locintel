namespace LocIntel.Modules.Marketplace.Vendors.Api;

public sealed record VendorListResponse(
    IReadOnlyList<VendorSummary> Items,
    int Total,
    int? NextOffset
);
