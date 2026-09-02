namespace LocIntel.Modules.Entities.Entities.Api;

public sealed record EntityListResponse(
    IReadOnlyList<EntitySummary> Items,
    int Total,
    int? NextOffset
);
