using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>Case-to-incident edge; stamps the incident's ancestor path (ADR 2) so scope resolves here. Tier 3.</summary>
public sealed class CaseIncident : IPathScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid CaseId { get; init; }
    public required Guid IncidentId { get; init; }
    public required Guid SiteId { get; init; }
    public required LTree Path { get; init; }
    public required Guid AddedBy { get; init; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
}
