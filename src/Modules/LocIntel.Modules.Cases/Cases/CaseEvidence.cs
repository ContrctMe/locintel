using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>A Storage file held as evidence in a case (by id; ADR 19 owns the bytes). Tier 3 - the custody log keeps the removal.</summary>
public sealed class CaseEvidence : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid CaseId { get; init; }
    public required Guid FileId { get; init; }
    public string? Label { get; set; }
    public required Guid AddedBy { get; init; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
}
