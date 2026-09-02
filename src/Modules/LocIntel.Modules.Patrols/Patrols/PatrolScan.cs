using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Patrols.Patrols;

/// <summary>A checkpoint reached, with the guard's position and distance to the checkpoint when both are known. Append-only, tier 3.</summary>
public sealed class PatrolScan : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid PatrolId { get; init; }
    public required string Code { get; init; }
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.UtcNow;
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? DistanceMeters { get; init; }
    public string? Note { get; init; }
}
