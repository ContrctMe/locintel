using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Api;

public sealed record RecipientView(
    Guid VendorOrgId,
    string? VendorName,
    RecipientStatus Status,
    DateTimeOffset NotifiedAt
);
