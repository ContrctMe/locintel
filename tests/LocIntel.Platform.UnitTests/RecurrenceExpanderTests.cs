using LocIntel.Platform.Scheduling;

namespace LocIntel.Platform.UnitTests;

/// <summary>
/// Pure logic (ADR 38): RRULE expansion is deterministic given rule, zone,
/// and horizon - and its DST arithmetic is exactly the kind of logic worth
/// covering exhaustively. The projection pipeline around it is integration
/// territory; the arithmetic itself is unit territory.
/// </summary>
public class RecurrenceExpanderTests
{
    [Fact]
    public void Weekly_rule_lands_on_the_requested_days()
    {
        var occurrences = RecurrenceExpander.Expand(
            "FREQ=WEEKLY;BYDAY=MO,WE",
            anchorDate: new DateOnly(2026, 3, 2), // a Monday
            startLocal: new TimeOnly(9, 0),
            endLocal: new TimeOnly(17, 0),
            ianaTimeZone: "America/New_York",
            exDates: [],
            horizonStartUtc: new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero),
            horizonEndUtc: new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero)
        );
        Assert.Equal(2, occurrences.Count);
        Assert.All(
            occurrences,
            o =>
                Assert.Contains(
                    o.LocalDate.DayOfWeek,
                    new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }
                )
        );
    }

    [Fact]
    public void Spring_forward_keeps_wall_clock_hours()
    {
        // US DST starts 2026-03-08: 09:00 local is UTC-5 before, UTC-4 after
        var occurrences = RecurrenceExpander.Expand(
            "FREQ=DAILY",
            anchorDate: new DateOnly(2026, 3, 7),
            startLocal: new TimeOnly(9, 0),
            endLocal: new TimeOnly(17, 0),
            ianaTimeZone: "America/New_York",
            exDates: [],
            horizonStartUtc: new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero),
            horizonEndUtc: new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
        );
        var before = occurrences.First(o => o.LocalDate == new DateOnly(2026, 3, 7));
        var after = occurrences.First(o => o.LocalDate == new DateOnly(2026, 3, 9));
        Assert.Equal(14, before.StartUtc.Hour); // 09:00 EST = 14:00 UTC
        Assert.Equal(13, after.StartUtc.Hour); // 09:00 EDT = 13:00 UTC
    }

    [Fact]
    public void Exdates_carve_out_closures()
    {
        var occurrences = RecurrenceExpander.Expand(
            "FREQ=DAILY",
            anchorDate: new DateOnly(2026, 6, 1),
            startLocal: new TimeOnly(8, 0),
            endLocal: new TimeOnly(12, 0),
            ianaTimeZone: "Etc/UTC",
            exDates: [new DateOnly(2026, 6, 3)],
            horizonStartUtc: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            horizonEndUtc: new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero)
        );
        Assert.DoesNotContain(occurrences, o => o.LocalDate == new DateOnly(2026, 6, 3));
        Assert.Equal(4, occurrences.Count); // 5 days minus the exdate
    }

    [Fact]
    public void Overnight_windows_end_on_the_next_local_day()
    {
        var occurrence = RecurrenceExpander
            .Expand(
                "FREQ=DAILY",
                anchorDate: new DateOnly(2026, 6, 1),
                startLocal: new TimeOnly(22, 0),
                endLocal: new TimeOnly(2, 0),
                ianaTimeZone: "Etc/UTC",
                exDates: [],
                horizonStartUtc: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                horizonEndUtc: new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero)
            )
            .First();
        Assert.True(occurrence.EndUtc > occurrence.StartUtc);
        Assert.Equal(4, (occurrence.EndUtc - occurrence.StartUtc).TotalHours);
    }

    [Theory]
    [InlineData("FREQ=WEEKLY;BYDAY=MO", true)]
    [InlineData("not a rule", false)]
    public void Rule_validation(string rule, bool valid) =>
        Assert.Equal(valid, RecurrenceExpander.IsValidRule(rule));
}
