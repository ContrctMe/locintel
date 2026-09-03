namespace LocIntel.Modules.Network.Bulletins.Messages;

public sealed record SharedBulletinWithdrawn(Guid BulletinId, DateTimeOffset WithdrawnAt);
