using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Reporting.Data;

/// <summary>
/// Persist the intended object key BEFORE writing bytes. Failed attempts and ZIPs
/// are tier 3 ephemera. A ready PDF transfers byte ownership to Storage; keep its
/// provenance alongside the file tombstone. CreatedAt is a UTC instant.
/// </summary>
public sealed class ReportArtifact : IOrgScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required OrgId OrgId { get; init; }
    public required Guid JobId { get; init; }
    public Guid? ItemId { get; init; }
    public required int Revision { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public bool Ready { get; set; }
    public bool FilePublished { get; set; }
    public long Bytes { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
