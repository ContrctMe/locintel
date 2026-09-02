namespace LocIntel.Modules.Patrols.Patrols.Api;

public sealed record PatrolDayResponse(
    DateOnly BusinessDate,
    string Site,
    IReadOnlyList<ExpectedPatrol> Expected,
    IReadOnlyList<PatrolView> Patrols
);
