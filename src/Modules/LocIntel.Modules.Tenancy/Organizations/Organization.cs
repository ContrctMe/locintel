using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Tenancy.Organizations;

/// <summary>
/// Deletion tier 1 (ADR 25): lifecycle status, never deleted. Platform-global
/// table - it is the org anchor itself, so it carries no RLS (allowlisted in
/// the CI coverage assertion). Region names the silo the org's data lives in
/// (ADR 35); identity stays global, org data stays regional.
/// </summary>
public sealed class Organization
{
    public required OrgId Id { get; init; }
    public required string Name { get; set; }
    public required string Slug { get; init; }
    public required RegionId Region { get; init; }

    /// <summary>Auth-provider org id (e.g. WorkOS org) for SSO mapping (ADR 14).</summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// The vendor's own org: its members may hold platform:operate and reach
    /// across tenants (entitlement custody, suspension). Never settable via
    /// API - seeded/ops-configured only.
    /// </summary>
    public bool IsPlatform { get; init; }

    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;

    /// <summary>
    /// UTC instant (ADR 26): a manager asked to close the org. The org stays
    /// ACTIVE (cancelable) through the grace window; the sweep offboards it
    /// after Organizations:CloseGraceDays. Null = no closure pending.
    /// </summary>
    public DateTimeOffset? CloseRequestedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The row's xmin: the transaction that last wrote it. Every
    /// OrganizationUpserted carries it as SourceVersion so a read model can
    /// tell a stale event from a newer one (docs/cross-tenant-sharing.md).
    /// </summary>
    public uint Version { get; set; }
}

public enum OrganizationStatus
{
    Active,
    Suspended,
    Offboarding,
}
