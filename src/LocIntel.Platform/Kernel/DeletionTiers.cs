namespace LocIntel.Platform.Kernel;

/// <summary>
/// Deletion tier 2 of 3 (ADR 25): user-generated content that supports restore.
/// A global named query filter ("SoftDelete") excludes deleted rows by default.
/// Tier 1 (lifecycle status: sites close, orgs suspend) is a per-entity status
/// enum - not an interface, because each lifecycle is domain-specific.
/// Tier 3 (hard delete: join rows, tokens, ephemera) needs no marker.
/// GDPR erasure is a separate hard path regardless of tier.
/// </summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>
/// Marker for org-scoped entities. Drives the "Tenant" named query filter and
/// requires an RLS policy on the table (enforced by CI coverage assertion).
/// </summary>
public interface IOrgScoped
{
    OrgId OrgId { get; }
}
