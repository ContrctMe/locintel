using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Entitlements.Data;

/// <summary>Per-org assigned value for a catalog code (ADR 10). Absent row = catalog default.</summary>
public sealed class OrgEntitlement : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Code { get; init; }
    public required string Value { get; set; }

    /// <summary>Which IEntitlementSource wrote it ("manual", "stripe", ...).</summary>
    public required string Source { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// First-class exception (ADR 10): expiring, attributed, auditable - never a
/// mutable override field. An active exception wins over the assigned value.
/// </summary>
public sealed class EntitlementException : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Code { get; init; }
    public required string Value { get; init; }
    public required string Reason { get; init; }
    public required Guid GrantedBy { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Append-only usage event (metering consequence: append-then-rollup).</summary>
public sealed class UsageEvent : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Code { get; init; }
    public required long Amount { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>Compacted usage per calendar month (UTC). Live count = rollups + uncompacted events.</summary>
public sealed class MeterRollup : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Code { get; init; }
    public required DateOnly PeriodMonth { get; init; }
    public required long Amount { get; set; }
    public required DateTimeOffset CompactedThrough { get; set; }
}
