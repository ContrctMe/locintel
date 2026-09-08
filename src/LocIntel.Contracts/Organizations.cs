using LocIntel.Platform.Kernel;

namespace LocIntel.Contracts;

/// <summary>Cross-module read contract implemented by the Tenancy module.</summary>
public interface IOrganizationLookup : LocIntel.Platform.Messaging.IOrganizationEnumerator
{
    Task<OrgSummary?> FindBySlugAsync(string slug, CancellationToken ct = default);
    Task<OrgSummary?> FindByExternalIdAsync(string externalId, CancellationToken ct = default);
    Task<OrgSummary?> GetAsync(OrgId id, CancellationToken ct = default);

    /// <summary>
    /// All org ids - for platform enumerators fanning out per-org work
    /// (ADR 24). Declared by IOrganizationEnumerator, the narrow port
    /// PerOrgSweepService depends on.
    /// </summary>
    new Task<IReadOnlyList<OrgId>> ListIdsAsync(CancellationToken ct = default);
}

public sealed record OrgSummary(
    OrgId Id,
    string Name,
    string Slug,
    RegionId Region,
    string? ExternalId
);

/// <summary>
/// Cross-module contract: the module OWNING a limited resource reports current
/// usage so the entitlements module can run downgrade preflight (ADR 11)
/// without reaching into another module's tables.
/// </summary>
public interface IEntitlementUsageProbe
{
    /// <summary>The limit-shaped entitlement code this probe measures.</summary>
    string Code { get; }

    ValueTask<long> CurrentUsageAsync(OrgId org, CancellationToken ct = default);
}

/// <summary>
/// Integration event: an org was created or its master data changed. Published
/// by whatever writes organizations (tenant lifecycle, ingest, seeding);
/// consumed by modules keeping local read models - Identity's org_directory is
/// the first. This replication is ALSO the extraction pattern: when a module
/// becomes a service, its contract reads become event-fed projections like
/// this one instead of network calls.
/// </summary>
/// <param name="SourceVersion">
/// The owner row's version (its xmin) at publish time. Two events for one
/// org can reach a read model in either order; the handler applies an event
/// only when this is newer than what it last applied (ProjectionVersion).
/// </param>
public sealed record OrganizationUpserted(
    OrgId OrgId,
    string Name,
    string Slug,
    RegionId Region,
    string? ExternalId,
    long SourceVersion,
    string Status = "Active",
    bool IsPlatform = false
);

/// <summary>Intent-level audit (ADR 12): modules publish these deliberately, in business language.</summary>
public sealed record RecordDomainAudit(string EventName, string PayloadJson);

/// <summary>Authorization decision audit: denials always (floor), grants per policy.</summary>
public sealed record RecordAuthzAudit(string Action, string Outcome, string ScopeSummary);

/// <summary>Read/access audit: high-volume, async path (ADR 13).</summary>
public sealed record RecordAccessAudit(string Method, string Path, int StatusCode);

/// <summary>Cross-module read contracts for ingest (implemented by Tenancy / Storage; consumed above the ladder).</summary>
public interface ISiteLookup
{
    /// <summary>The live sites behind these external ids - the file's rows, never the whole org.</summary>
    Task<IReadOnlyList<SiteSnapshot>> ListSitesAsync(
        IReadOnlyCollection<string> externalIds,
        CancellationToken ct = default
    );

    /// <summary>Resolve incident-import references by name or external id, ignoring case.</summary>
    Task<IReadOnlyList<SiteSnapshot>> ResolveSitesAsync(
        IReadOnlyCollection<string> references,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<NodeSnapshot>> ListNodesAsync(CancellationToken ct = default);

    /// <summary>How many sites the org has (the plan limit's "current", checked once per batch).</summary>
    Task<long> CountSitesAsync(CancellationToken ct = default);
}

public sealed record SiteSnapshot(
    SiteId Id,
    string? ExternalId,
    string Name,
    string TimeZone,
    string Status
);

/// <summary>NamePath is the human path ("East/Boston") ingest files address nodes by.</summary>
public sealed record NodeSnapshot(Guid Id, string NamePath);

public interface IStoredFileLookup
{
    Task<StoredFileInfo?> GetAsync(Guid fileId, CancellationToken ct = default);
}

public sealed record StoredFileInfo(
    Guid Id,
    string Key,
    string Status,
    string ContentType,
    string Name
);

/// <summary>
/// Cross-module WRITE (ADR 17/18): ingest never touches Tenancy's tables -
/// it requests changes over the outbox and Tenancy applies them. Closing is
/// an action with a domain event, never a delete.
/// </summary>
public sealed record SiteChangeRequested(
    string Action, // create | update | close
    string ExternalId,
    string Name,
    string TimeZone,
    Guid? NodeId,
    Guid? CapacityReservationId = null
);

/// <summary>
/// Cross-module write (ADR 17): Tenancy created the org; Identity provisions
/// the founder's membership, Owner bootstrap, and provider-side membership.
/// </summary>
public sealed record ProvisionFounderMembership(Guid UserId, OrgId OrgId);

/// <summary>
/// Offboarding export (lifecycle tail): each module serializes its own slice
/// - the plugin direction, like the usage probes. The Storage module
/// assembles the archive.
/// </summary>
public interface IOrgDataExporter
{
    /// <summary>Archive entry name, e.g. "tenancy" -> tenancy.json.</summary>
    string Section { get; }

    Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default);
}

/// <summary>Assemble the org's export archive (handled by Storage; tenant on the envelope).</summary>
public sealed record ExportOrgData(Guid RequestedBy, Guid? AdmissionId = null);

/// <summary>
/// Org deletion fan-out, one command per owning module so each Wolverine
/// chain stays single-DbContext (multi-context chains are the known trap).
/// Handlers run envelope-tenanted and are idempotent. Audit is deliberately
/// absent: the trail outlives the org (retention ages it out).
/// </summary>
public sealed record PurgeOrgSites;

public sealed record PurgeOrgFiles;

public sealed record PurgeOrgEntitlements;

public sealed record PurgeOrgIngest;

/// <summary>Webhook CONFIG purges with the org; the audit trail itself stays (ADR 25/40).</summary>
public sealed record PurgeOrgWebhooks;

public sealed record PurgeOrgChecklists;

public sealed record PurgeOrgSpatial;

/// <summary>The org is gone: read models drop it, provider directory follows.</summary>
public sealed record OrganizationDeleted(OrgId OrgId, string? ExternalId);
