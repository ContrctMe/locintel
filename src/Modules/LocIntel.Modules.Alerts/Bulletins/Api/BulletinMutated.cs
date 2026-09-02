namespace LocIntel.Modules.Alerts.Bulletins.Api;

public sealed record BulletinMutated(Guid Id, BulletinStatus Status, DateTimeOffset? WithdrawnAt);
