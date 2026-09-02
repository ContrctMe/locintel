namespace LocIntel.Modules.Alerts.Bulletins.Api;

public sealed record BulletinListResponse(
    IReadOnlyList<BulletinView> Items,
    int Total,
    int? NextOffset
);
