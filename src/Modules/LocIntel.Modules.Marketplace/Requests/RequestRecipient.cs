using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The requester's list of who a broadcast was sent to - the recipient
/// list as DATA on the owner's side (ADR 48). The vendor's own standing
/// lives on its VendorAssignment; this row mirrors it from events. Tier 3.
/// NotifiedAt / RespondedAt are UTC instants.
/// </summary>
public sealed class RequestRecipient : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required OrgId VendorOrgId { get; init; }
    public required Guid RequestId { get; init; }
    public RecipientStatus Status { get; set; } = RecipientStatus.Invited;
    public DateTimeOffset NotifiedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
}
