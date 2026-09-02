using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Vendors;

/// <summary>
/// A license, insurance certificate, or certification with an expiry.
/// Readable across orgs while the vendor's profile is published (a buyer
/// checks before hiring); expired credentials block new assignments.
/// Tier 3. ExpiresAt / CreatedAt are UTC instants.
/// </summary>
public sealed class VendorCredential
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required CredentialKind Kind { get; init; }
    public required string Label { get; set; }
    public string? Number { get; set; }
    public string? Jurisdiction { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
