using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Vendors;

/// <summary>A requester org's curated list: preferred (routed first) or blocked (never). Tier 3.</summary>
public sealed class PreferredVendor : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required OrgId VendorOrgId { get; init; }
    public string[] Categories { get; set; } = [];
    public string? Notes { get; set; }
    public bool Blocked { get; set; }
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
