using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Reporting.Data;

/// <summary>Tier 3 ephemera, deleted with its job. GeneratedAt is a UTC instant.</summary>
public sealed class ReportItem : IOrgScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required OrgId OrgId { get; init; }
    public required Guid JobId { get; init; }
    public required Guid[] SiteIds { get; init; }
    public ReportItemState State { get; set; } = ReportItemState.Queued;
    public int Attempt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string WarningsJson { get; set; } = "[]";
    public string DependenciesJson { get; set; } = "{}";
}
