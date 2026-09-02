namespace LocIntel.Modules.Marketplace.Requests.Api;

public sealed record RequestListResponse(
    IReadOnlyList<RequestSummary> Items,
    int Total,
    int? NextOffset
);
