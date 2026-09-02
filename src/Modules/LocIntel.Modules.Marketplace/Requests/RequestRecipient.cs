using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// A vendor a broadcast request was sent to. Two-party row (requester +
/// this vendor) so the vendor can see the request before it has a
/// CounterpartyOrgId (the vendor). Tier 3. NotifiedAt / RespondedAt are UTC instants.
/// </summary>
public sealed class RequestRecipient : ITwoPartyScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }

    /// <summary>The vendor (ITwoPartyScoped's counterparty; NOT NULL in the table, nullable only to fit the shape).</summary>
    public required OrgId? CounterpartyOrgId { get; init; }

    /// <summary>The vendor, non-null for reads: the column is NOT NULL even though the shape's type is not.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public OrgId VendorOrgId => CounterpartyOrgId!.Value;
    public required Guid RequestId { get; init; }
    public RecipientStatus Status { get; set; } = RecipientStatus.Invited;
    public DateTimeOffset NotifiedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
}
