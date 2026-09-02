using LocIntel.Modules.Alerts.Alerts;

namespace LocIntel.Modules.Alerts.Bulletins.Api;

/// <summary>ScopePath targets a hierarchy subtree (null = org-wide); ExpiresAt defaults to 14 days, max 90.</summary>
public sealed record IssueBulletinRequest(
    BulletinKind Kind,
    AlertSeverity Severity,
    string Title,
    string Body,
    string? ScopePath = null,
    Guid? EntityId = null,
    Guid? IncidentId = null,
    Guid? CaseId = null,
    DateTimeOffset? ExpiresAt = null
);
