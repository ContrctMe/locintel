using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>
/// The crime-intelligence FACT row: something that happened at a site.
/// Stamps at write time (ADR 2/4/26): the site's ancestor path keyed by
/// HierarchyId, and the site-local business date of OccurredAt. Rollups
/// group on those stamps and never re-derive them after a re-parent.
/// Deletion tier 2 (soft delete with restore, ADR 25); LegalHold blocks
/// deletion outright - the record is evidence.
/// Temporal kinds: OccurredAt / ReportedAt / ClosedAt / UpdatedAt / DeletedAt
/// are UTC instants; BusinessDate is the stamped site-local date.
/// </summary>
public sealed class Incident : IPathScoped, ISoftDeletable
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid SiteId { get; init; }
    public required Guid HierarchyId { get; init; }

    /// <summary>Ancestor path as of write time (ADR 2), keyed by HierarchyId (ADR 4).</summary>
    public required LTree Path { get; init; }

    public required IncidentCategory Category { get; set; }
    public required IncidentSeverity Severity { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;
    public required IncidentSource Source { get; init; }
    public required string Title { get; set; }
    public string Narrative { get; set; } = "";

    /// <summary>Where within the site ("electronics aisle", "loading dock").</summary>
    public string? LocationDetail { get; set; }

    /// <summary>UTC instant (ADR 26 kind 1).</summary>
    public required DateTimeOffset OccurredAt { get; set; }

    /// <summary>Stamped site-local business date of OccurredAt (ADR 26 kind 3).</summary>
    public required DateOnly BusinessDate { get; set; }

    /// <summary>UTC instant.</summary>
    public DateTimeOffset ReportedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>User id, the API key id when Source is Api, or Guid.Empty for an anonymous tip.</summary>
    public required Guid ReportedBy { get; init; }

    /// <summary>How to reach an anonymous tipster who chose to leave a way (Source = Tip); never shown publicly.</summary>
    public string? ReporterContact { get; set; }

    public decimal? LossAmount { get; set; }
    public decimal? RecoveredAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? PoliceReportNumber { get; set; }
    public string[] Tags { get; set; } = [];

    /// <summary>UTC instant.</summary>
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public string? ClosureReason { get; set; }

    /// <summary>Evidence under hold cannot be deleted; purge jobs must respect it too.</summary>
    public bool LegalHold { get; set; }

    /// <summary>UTC instant.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC instant; set = in the trash (tier 2).</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
