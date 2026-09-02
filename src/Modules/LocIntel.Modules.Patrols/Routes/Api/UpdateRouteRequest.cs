namespace LocIntel.Modules.Patrols.Routes.Api;

public sealed record UpdateRouteRequest(
    string Name,
    Checkpoint[] Checkpoints,
    int? ExpectedMinutes,
    bool Archived
);
