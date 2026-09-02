using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>
/// An investigation: incidents, people, evidence, tasks, and notes under one
/// lead. Not path-scoped (a case spans sites); visibility is membership or a
/// linked incident in scope (CaseVisibility), or cases:manage. Legal hold
/// cascades to the evidence files over the outbox. Deletion tier 2.
/// Temporal kinds: all columns are UTC instants.
/// </summary>
public sealed class Case : IOrgScoped, ISoftDeletable
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Title { get; set; }
    public string Summary { get; set; } = "";
    public CaseStatus Status { get; set; } = CaseStatus.Open;
    public CasePriority Priority { get; set; } = CasePriority.Medium;
    public Guid? LeadId { get; set; }
    public CaseDisposition? Disposition { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public string? ClosureNote { get; set; }
    public bool LegalHold { get; set; }
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
