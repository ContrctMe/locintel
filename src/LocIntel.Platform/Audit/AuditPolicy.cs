using LocIntel.Platform.Kernel;

namespace LocIntel.Platform.Audit;

/// <summary>
/// Effective per-org audit policy (ADR 12): application config intersected
/// with the entitlement ceiling. The PLATFORM FLOOR is structural, not
/// configuration: domain events, authz denials, and change diffs have no off
/// switch anywhere in this type - only grants- and read-logging are choices.
/// </summary>
public sealed record AuditPolicy(bool LogGrants, bool LogReads, int RetentionDays)
{
    public static readonly AuditPolicy Floor = new(false, false, 90);
}

public interface IAuditPolicyProvider
{
    ValueTask<AuditPolicy> GetAsync(OrgId org, CancellationToken ct = default);
}
