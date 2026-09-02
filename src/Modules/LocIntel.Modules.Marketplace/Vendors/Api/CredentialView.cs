using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Vendors.Api;

public sealed record CredentialView(
    Guid Id,
    CredentialKind Kind,
    string Label,
    string? Number,
    string? Jurisdiction,
    DateTimeOffset ExpiresAt,
    bool Expired
);
