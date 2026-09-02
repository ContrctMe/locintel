using LocIntel.Modules.Alerts.Alerts;

namespace LocIntel.Modules.Alerts.Bulletins.Api;

public sealed record BulletinView(
    Guid Id,
    BulletinKind Kind,
    AlertSeverity Severity,
    string Title,
    string Body,
    string? ScopePath,
    Guid? EntityId,
    Guid? IncidentId,
    Guid? CaseId,
    Guid IssuedBy,
    string? Issuer,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    BulletinStatus Status,
    bool Active,
    bool Acknowledged,
    int Acknowledgements
);
