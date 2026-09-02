namespace LocIntel.Modules.Alerts.Alerts.Api;

/// <summary>The dashboard and nav badge numbers, for the caller's scope and reads.</summary>
public sealed record AlertSummary(int Unread, int ActiveBulletins, int UnacknowledgedBulletins);
