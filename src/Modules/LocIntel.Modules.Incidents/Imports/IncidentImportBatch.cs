using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Incidents.Imports;

/// <summary>
/// A staged CSV of historical incidents (ADR 18's shape: stage, preview,
/// commit; nothing lands until commit). Lifecycle status, never deleted.
/// CreatedAt / CommittedAt are UTC instants.
/// </summary>
public sealed class IncidentImportBatch : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid FileId { get; init; }
    public required string FileName { get; init; }
    public ImportStatus Status { get; set; } = ImportStatus.Staged;
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CommittedAt { get; set; }
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
}
