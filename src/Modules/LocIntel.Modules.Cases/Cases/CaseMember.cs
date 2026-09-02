using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>Who works the case; membership is the need-to-know for it. Tier 3.</summary>
public sealed class CaseMember : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid CaseId { get; init; }
    public required Guid UserId { get; init; }
    public required CaseMemberRole Role { get; set; }
    public required Guid AddedBy { get; init; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
}
