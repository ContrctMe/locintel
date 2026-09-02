namespace LocIntel.Modules.Patrols.Routes.Api;

public sealed record CreateRouteRequest(
    Guid SiteId,
    string Name,
    Checkpoint[] Checkpoints,
    int? ExpectedMinutes = null
);
