using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>Case-to-entity edge by id only; names resolve through the need-to-know directory at read time. Tier 3.</summary>
public sealed class CaseEntity : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid CaseId { get; init; }
    public required Guid EntityId { get; init; }
    public string? Note { get; set; }
    public required Guid AddedBy { get; init; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
}
