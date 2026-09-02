namespace LocIntel.Modules.Patrols.Routes.Api;

public sealed record ScheduleView(
    Guid Id,
    string RRule,
    DateOnly AnchorDate,
    TimeOnly StartLocal,
    DateOnly[] ExDates,
    bool Active
);
