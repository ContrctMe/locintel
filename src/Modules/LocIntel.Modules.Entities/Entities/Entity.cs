using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Entities.Entities;

/// <summary>
/// A person of interest, vehicle, or organized group. The most legally
/// sensitive row in the product: it is an allegation about someone real.
/// Not path-scoped - people move between sites - so access is need-to-know
/// (EntityVisibility): via a linked incident in the reader's scope, an
/// explicit time-boxed grant, or entities:manage. Every detail read is a
/// domain audit event. Retention is mandatory (ExpiresAt); the sweep trashes
/// expired rows unless LegalHold is set. Deletion tier 2 (ADR 25).
/// Temporal kinds: ExpiresAt / CreatedAt / UpdatedAt / DeletedAt are UTC instants.
/// </summary>
public sealed class Entity : IOrgScoped, ISoftDeletable
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required EntityKind Kind { get; init; }
    public EntityStatus Status { get; set; } = EntityStatus.Suspected;

    /// <summary>Person: name or primary alias; vehicle: plate + make/model; group: name.</summary>
    public required string DisplayName { get; set; }
    public string[] Aliases { get; set; } = [];

    /// <summary>Structured descriptors as a flat JSON object of strings (height, plate, color...). jsonb.</summary>
    public string DescriptorsJson { get; set; } = "{}";

    /// <summary>Why this record exists - the basis a reviewer can weigh.</summary>
    public string Summary { get; set; } = "";

    /// <summary>UTC instant; the retention sweep trashes the row after this unless held.</summary>
    public required DateTimeOffset ExpiresAt { get; set; }
    public bool LegalHold { get; set; }
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
