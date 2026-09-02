namespace LocIntel.Platform.Kernel;

/// <summary>
/// The resolved tenant of the current unit of work. Populated from the request
/// principal (HTTP) or the message envelope (Wolverine) - org is NEVER ambient
/// beyond this explicitly-scoped object (ADR 5/24).
/// </summary>
public interface ITenantContext
{
    /// <summary>Null only for platform-level work that owns no org (rare, deliberate).</summary>
    OrgId? OrgId { get; }
    RegionId Region { get; }
}

/// <summary>Mutable holder registered per-scope; set once by middleware.</summary>
public sealed class TenantContext : ITenantContext
{
    public OrgId? OrgId { get; private set; }
    public RegionId Region { get; private set; } = RegionId.Default;

    public void Set(OrgId orgId, RegionId region)
    {
        if (OrgId is not null)
            throw new InvalidOperationException("Tenant context is already set for this scope.");
        OrgId = orgId;
        Region = region;
    }
}
