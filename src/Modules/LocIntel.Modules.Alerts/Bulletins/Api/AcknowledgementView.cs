namespace LocIntel.Modules.Alerts.Bulletins.Api;

public sealed record AcknowledgementView(
    Guid Id,
    Guid UserId,
    string? User,
    Guid? SiteId,
    string? Note,
    DateTimeOffset AcknowledgedAt
);
