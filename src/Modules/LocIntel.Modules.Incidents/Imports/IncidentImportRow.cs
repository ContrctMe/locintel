using LocIntel.Modules.Incidents.Incidents;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Incidents.Imports;

/// <summary>One parsed line, resolved and validated; Errors is why it will not land. Tier 3 with its batch.</summary>
public sealed class IncidentImportRow : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid BatchId { get; init; }
    public required int RowNumber { get; init; }
    public required string SiteRef { get; init; }
    public Guid? SiteId { get; init; }
    public IncidentCategory? Category { get; init; }
    public IncidentSeverity? Severity { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public required string Title { get; init; }
    public string Narrative { get; init; } = "";
    public decimal? LossAmount { get; init; }
    public string? PoliceReportNumber { get; init; }
    public string[] Tags { get; init; } = [];
    public string[] Errors { get; init; } = [];
    public Guid? IncidentId { get; set; }
}
