using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Reporting.Data;

/// <summary>Tier 3 ephemera: hard deleted after retention. All timestamps are UTC instants.</summary>
public sealed class ReportJob : IOrgScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required OrgId OrgId { get; init; }
    public required Guid RequestedBy { get; init; }
    public required string ReportType { get; init; }
    public required int DefinitionVersion { get; init; }
    public required ReportMode Mode { get; init; }
    public required ReportSelection Selection { get; init; }
    public required string OptionsJson { get; init; }
    public required Guid[] SiteIds { get; init; }
    public ReportJobState State { get; set; } = ReportJobState.Queued;
    public string? ErrorCode { get; set; }
    public int Revision { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? MetadataExpiresAt { get; set; }
    public List<ReportItem> Items { get; set; } = [];
    public List<ReportArtifact> Artifacts { get; set; } = [];
}
