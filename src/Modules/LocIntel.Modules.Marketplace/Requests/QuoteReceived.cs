using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The requester's projection of a vendor's quote (ADR 48): the vendor owns
/// the Quote; this row is what the requester compares and awards. QuoteId is
/// the vendor's id, the correlation for QuoteDecided. Tier 1 (status).
/// </summary>
public sealed class QuoteReceived : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid RequestId { get; init; }
    public required Guid QuoteId { get; init; }
    public required OrgId VendorOrgId { get; init; }
    public required string VendorName { get; set; }
    public required decimal Amount { get; set; }
    public required string Currency { get; init; }
    public string? Notes { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Submitted;
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
