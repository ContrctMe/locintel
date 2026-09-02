using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Checklists.Checklists;

/// <summary>
/// One ticked box: template item N, at a site, on that site's business date
/// (ADR 26 kind 3 - stamped site-local, never derived later). Unchecking
/// deletes the row (tier 3); the domain audit trail keeps the story.
/// </summary>
public sealed class ChecklistItemCheck : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid TemplateId { get; init; }
    public required Guid SiteId { get; init; }

    /// <summary>Stamped site-local business date (ADR 26).</summary>
    public required DateOnly BusinessDate { get; init; }

    public required int ItemIndex { get; init; }
    public required Guid CheckedBy { get; init; }

    /// <summary>UTC instant (ADR 26).</summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}
