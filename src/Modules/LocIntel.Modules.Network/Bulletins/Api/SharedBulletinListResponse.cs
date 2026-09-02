namespace LocIntel.Modules.Network.Bulletins.Api;

public sealed record SharedBulletinListResponse(
    IReadOnlyList<SharedBulletinView> Items,
    int Total,
    int? NextOffset
);
