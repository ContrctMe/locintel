namespace LocIntel.Modules.Entities.Entities.Api;

public sealed record EntityLinkView(
    Guid Id,
    Guid IncidentId,
    Guid SiteId,
    string? IncidentTitle,
    string? IncidentStatus,
    DateTimeOffset? OccurredAt,
    LinkRole Role,
    string? Note,
    DateTimeOffset LinkedAt
);
