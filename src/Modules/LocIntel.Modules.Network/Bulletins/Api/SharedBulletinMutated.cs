namespace LocIntel.Modules.Network.Bulletins.Api;

public sealed record SharedBulletinMutated(Guid Id, DateTimeOffset? WithdrawnAt);
