using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>
/// Chain of custody: append-only, one row per touch of a piece of evidence
/// (added, downloaded, exported, removed, held). Never edited or deleted
/// short of org purge; the prosecution package carries it. At is a UTC instant.
/// </summary>
public sealed class CustodyEvent : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid CaseId { get; init; }
    public required Guid FileId { get; init; }
    public required CustodyAction Action { get; init; }
    public required Guid ActorId { get; init; }
    public required string ActorTier { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
