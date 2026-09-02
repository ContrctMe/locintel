using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Ingest.Data;

/// <summary>
/// One staged import (ADR 18): rows land here, the diff is computed against
/// live sites, an admin previews the counts, and only COMMIT publishes the
/// changes. Nothing touches site data before commit.
/// </summary>
public sealed class ImportBatch : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Source { get; init; } // "upload" | connector name
    public BatchStatus Status { get; set; } = BatchStatus.Staged;
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Counts { get; set; } = "{}";
}

public enum BatchStatus
{
    Staged,
    Committed,
    Discarded,
}

/// <summary>A parsed row with its computed action - the diff preview's unit.</summary>
public sealed class StagedSite : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid BatchId { get; init; }
    public required string ExternalId { get; init; }
    public required string Name { get; init; }
    public required string TimeZone { get; init; }
    public required string NodePath { get; init; }
    public Guid? NodeId { get; set; }
    public required string SourceStatus { get; init; } // open | closed
    public required string Action { get; set; } // create | update | close | unchanged | invalid
    public string[] Errors { get; set; } = [];
    public string[] Changes { get; set; } = [];
}

/// <summary>
/// A pull connector (ADR 18/31): per-org source with envelope-encrypted
/// credentials. Deletion tier 3.
/// </summary>
public sealed class SiteConnector : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Name { get; set; }
    public required string Type { get; init; } // "json-http" in v1; forks add types
    public required string Url { get; set; }
    public required byte[] EncryptedCredentials { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Null = manual-only. Otherwise the schedule enumerator syncs when this many hours have passed.</summary>
    public int? SyncIntervalHours { get; set; }
}
