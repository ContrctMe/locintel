namespace LocIntel.Modules.Alerts.Bulletins.Api;

public sealed record BulletinIssued(Guid Id, DateTimeOffset ExpiresAt);
