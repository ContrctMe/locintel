namespace LocIntel.Modules.Alerts.Bulletins.Api;

public sealed record BulletinAcknowledged(Guid Id, DateTimeOffset AcknowledgedAt);
