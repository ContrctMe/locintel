using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// Local read model of org master data, fed by OrganizationUpserted events.
/// This is what BREAKS the Identity -> Tenancy contract dependency: login and
/// /me read this table, never Tenancy's. Platform-global (no RLS) - it is
/// consulted before any tenant context exists, like users and memberships.
/// </summary>
public sealed class OrgDirectoryEntry
{
    public required OrgId OrgId { get; init; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public required RegionId Region { get; set; }
    public string? ExternalId { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsPlatform { get; set; }
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
