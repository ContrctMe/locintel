namespace LocIntel.Modules.Patrols.Routes.Api;

public sealed record RouteView(
    Guid Id,
    Guid SiteId,
    string Name,
    Checkpoint[] Checkpoints,
    int ExpectedMinutes,
    bool Archived,
    IReadOnlyList<ScheduleView> Schedules
);
