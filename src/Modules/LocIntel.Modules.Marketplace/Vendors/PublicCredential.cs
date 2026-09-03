using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Vendors;

/// <summary>What any buyer may know about a vendor's credential: everything but the number.</summary>
public sealed record PublicCredential(
    Guid Id,
    CredentialKind Kind,
    string Label,
    string? Jurisdiction,
    DateTimeOffset ExpiresAt
);
