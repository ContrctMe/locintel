using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>
/// Links a stored file (Storage module, by id only - ADR 19 owns the bytes,
/// lifecycle, and holds) to an incident. Join row: deletion tier 3 (hard).
/// AddedAt is a UTC instant.
/// </summary>
public sealed class IncidentAttachment : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid IncidentId { get; init; }
    public required Guid FileId { get; init; }
    public string? Label { get; set; }
    public required Guid AddedBy { get; init; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
}
