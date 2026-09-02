namespace LocIntel.Modules.Alerts.Alerts.Api;

public sealed record AlertView(
    Guid Id,
    AlertKind Kind,
    AlertSeverity Severity,
    string Title,
    string Body,
    Guid? IncidentId,
    Guid? BulletinId,
    Guid? EntityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt
);
