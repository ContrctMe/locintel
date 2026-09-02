using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>
/// An investigator's note on an incident. User content: deletion tier 2
/// (soft delete; the incident's trail keeps the story). CreatedAt/DeletedAt
/// are UTC instants.
/// </summary>
public sealed class IncidentNote : IOrgScoped, ISoftDeletable
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid IncidentId { get; init; }
    public required Guid AuthorId { get; init; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
