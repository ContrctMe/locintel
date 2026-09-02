namespace LocIntel.Modules.Alerts.Alerts.Api;

public sealed record AlertListResponse(
    IReadOnlyList<AlertView> Items,
    int Total,
    int? NextOffset,
    int Unread
);
