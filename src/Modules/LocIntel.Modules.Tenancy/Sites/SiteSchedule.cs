using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// A wall-clock recurring rule (ADR 26/27): RFC 5545 RRULE + local times,
/// resolved against the SITE's zone. The rule is the stored truth; the
/// occurrence projection (ADR 28) is derived and rebuildable. EXDATEs carry
/// holiday closures. Deletion tier 3: hard delete, the rule's effect on
/// history lives in the projection and audit.
/// </summary>
public sealed class SiteSchedule : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required SiteId SiteId { get; init; }
    public required string Name { get; set; }
    public required string RRule { get; set; }
    public required DateOnly AnchorDate { get; set; }
    public required TimeOnly OpensLocal { get; set; }
    public required TimeOnly ClosesLocal { get; set; }
    public DateOnly[] ExDates { get; set; } = [];

    public static SiteSchedule Create(
        OrgId orgId,
        SiteId siteId,
        string name,
        string rrule,
        DateOnly anchorDate,
        TimeOnly opens,
        TimeOnly closes
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrgId = orgId,
            SiteId = siteId,
            Name = name,
            RRule = rrule,
            AnchorDate = anchorDate,
            OpensLocal = opens,
            ClosesLocal = closes,
        };
}

/// <summary>
/// One materialized open window (ADR 28): the indexed, queryable projection of
/// the rules over a rolling horizon. LocalDate is the site-local business date
/// (ADR 26), stamped at materialization.
/// </summary>
public sealed class SiteOpenWindow : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required SiteId SiteId { get; init; }
    public required Guid ScheduleId { get; init; }
    public required DateTimeOffset StartsAtUtc { get; init; }
    public required DateTimeOffset EndsAtUtc { get; init; }
    public required DateOnly LocalDate { get; init; }
}
