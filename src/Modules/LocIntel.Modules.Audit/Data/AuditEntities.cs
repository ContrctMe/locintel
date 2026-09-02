using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Audit.Data;

/// <summary>Intent-level entries: "site 42 closed by Jane" (ADR 12).</summary>
public sealed class DomainLogEntry
{
    public required Guid Id { get; init; }
    public required Guid OrgId { get; init; }
    public required string ActorTier { get; init; }
    public Guid? ActorId { get; init; }
    public required string EventName { get; init; }
    public required string Payload { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>Authorization decisions: the highest-value rows (denials answer "why can't she see this?").</summary>
public sealed class AuthzLogEntry
{
    public required Guid Id { get; init; }
    public required Guid OrgId { get; init; }
    public required string ActorTier { get; init; }
    public Guid? ActorId { get; init; }
    public required string Action { get; init; }
    public required string Outcome { get; init; }
    public required string ScopeSummary { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>
/// Read/access rows - orders of magnitude higher volume than the others
/// (ADR 13); written async off the durable queue. Plain table in v1: the
/// retention purge is the seam where native monthly partitioning slots in
/// when volume demands it.
/// </summary>
public sealed class AccessLogEntry
{
    public required Guid Id { get; init; }
    public required Guid OrgId { get; init; }
    public required string ActorTier { get; init; }
    public Guid? ActorId { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required int StatusCode { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>
/// The configurable HALF of the policy (ADR 12). The floor (diffs, domain
/// events, denials) is structural - deliberately unrepresentable here.
/// Changes to this row are themselves audited as domain events.
/// </summary>
public sealed class OrgAuditConfig : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public bool LogGrants { get; set; }
    public bool LogReads { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
