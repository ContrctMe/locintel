namespace LocIntel.Modules.Patrols.Routes.Api;

/// <summary>StartLocal is the site's wall clock ("22:00"); the rule expands in the site's zone (ADR 27).</summary>
public sealed record CreateScheduleRequest(
    string RRule,
    DateOnly AnchorDate,
    TimeOnly StartLocal,
    DateOnly[]? ExDates = null
);
