using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Vendors.Api;

public sealed record AddCredentialRequest(
    CredentialKind Kind,
    string Label,
    DateTimeOffset ExpiresAt,
    string? Number = null,
    string? Jurisdiction = null
);
