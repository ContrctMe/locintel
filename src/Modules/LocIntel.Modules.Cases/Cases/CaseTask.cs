using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>Work item on a case. User content: tier 2. DueAt / DoneAt / CreatedAt / DeletedAt are UTC instants.</summary>
public sealed class CaseTask : IOrgScoped, ISoftDeletable
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid CaseId { get; init; }
    public required string Title { get; set; }
    public Guid? AssigneeId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public Guid? DoneBy { get; set; }
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
