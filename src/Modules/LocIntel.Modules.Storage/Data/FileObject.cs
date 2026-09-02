using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Storage.Data;

/// <summary>
/// A stored file's lifecycle (ADR 19): ticket issued -> uploaded -> scanned ->
/// clean (downloadable) or quarantined (never downloadable). Legal hold blocks
/// erasure; erasure removes bytes and derivatives, keeps the row as an
/// auditable tombstone, and emits a domain event.
/// </summary>
public sealed class FileObject : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public required long MaxBytes { get; init; }
    public FileStatus Status { get; set; } = FileStatus.PendingUpload;
    public bool LegalHold { get; set; }
    public string? PreviewKey { get; set; }
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScannedAt { get; set; }

    /// <summary>UTC instant (ADR 26): when it entered the trash; null unless Status is Deleted.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

public enum FileStatus
{
    PendingUpload,
    Uploaded,
    Clean,
    Quarantined,
    Erased,

    /// <summary>In the trash (ADR 25 tier 2): bytes retained, restorable until the window closes.</summary>
    Deleted,
}
