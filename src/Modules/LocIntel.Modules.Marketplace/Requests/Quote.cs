using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>A vendor's price for a broadcast request. Two-party row. Tier 1 (status). ValidUntil / CreatedAt are UTC instants.</summary>
public sealed class Quote : ITwoPartyScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }

    /// <summary>The vendor (ITwoPartyScoped's counterparty; NOT NULL in the table, nullable only to fit the shape).</summary>
    public required OrgId? CounterpartyOrgId { get; init; }

    /// <summary>The vendor, non-null for reads: the column is NOT NULL even though the shape's type is not.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public OrgId VendorOrgId => CounterpartyOrgId!.Value;
    public required Guid RequestId { get; init; }
    public required decimal Amount { get; set; }
    public required string Currency { get; init; }
    public string? Notes { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Submitted;
    public required Guid SubmittedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
