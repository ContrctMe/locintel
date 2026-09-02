using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// A vendor a broadcast request was sent to. Two-party row (requester +
/// this vendor) so the vendor can see the request before it has a
/// VendorOrgId. Tier 3. NotifiedAt / RespondedAt are UTC instants.
/// </summary>
public sealed class RequestRecipient
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required OrgId VendorOrgId { get; init; }
    public required Guid RequestId { get; init; }
    public RecipientStatus Status { get; set; } = RecipientStatus.Invited;
    public DateTimeOffset NotifiedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
}
