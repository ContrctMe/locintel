using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Checklists.Checklists;

/// <summary>
/// A recurring per-site task list (ADR 45): the ops archetype's core object.
/// Applies DAILY at every site the ScopePath covers (null = all sites) -
/// daily is the archetype's overwhelming case (opening/closing lists);
/// forks add weekly/RRULE recurrence when a vertical demands it.
/// Deletion tier 3 (hard): configuration, the completion trail is the record.
/// </summary>
public sealed class ChecklistTemplate : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Name { get; set; }

    /// <summary>The task lines, in order. Item identity is positional; edits create a new template version in spirit - keep them stable.</summary>
    public string[] Items { get; set; } = [];

    /// <summary>ltree prefix limiting which sites this applies to; null = the whole org.</summary>
    public string? ScopePath { get; set; }

    public required Guid CreatedBy { get; init; }

    /// <summary>UTC instant (ADR 26).</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
