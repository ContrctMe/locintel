using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Entities.Entities;

/// <summary>
/// The fourth gate's explicit half: a manager shares ONE entity with ONE
/// user for a bounded time, with a reason (the audit trail carries it).
/// Hard delete (tier 3) - revocation is itself a domain event.
/// ExpiresAt / CreatedAt are UTC instants.
/// </summary>
public sealed class EntityAccessGrant : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid EntityId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid GrantedBy { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
