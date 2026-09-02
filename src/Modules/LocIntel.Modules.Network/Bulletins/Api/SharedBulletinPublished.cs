namespace LocIntel.Modules.Network.Bulletins.Api;

public sealed record SharedBulletinPublished(Guid Id, DateTimeOffset ExpiresAt);
