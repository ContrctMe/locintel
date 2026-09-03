using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The vendor's OWN quote on a broadcast (ADR 48): written only by the
/// vendor; the requester holds a QuoteReceived projection of it and decides
/// there. Tier 1 (status). ValidUntil / CreatedAt are UTC instants.
/// </summary>
public sealed class Quote : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required OrgId RequesterOrgId { get; init; }
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
