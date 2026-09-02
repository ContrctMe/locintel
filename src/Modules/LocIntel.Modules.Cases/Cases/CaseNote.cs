using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>Investigator's note; the timeline is notes plus the audit trail. Tier 2.</summary>
public sealed class CaseNote : IOrgScoped, ISoftDeletable
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid CaseId { get; init; }
    public required Guid AuthorId { get; init; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
