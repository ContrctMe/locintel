using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace LocIntel.Platform.Scheduling;

/// <summary>
/// Server-authoritative RRULE expansion (ADR 27): wall-clock rules anchored in
/// the site's IANA zone, converted to UTC instants only AFTER expansion - the
/// only order that survives daylight saving. Clients display; they never
/// expand.
/// </summary>
public static class RecurrenceExpander
{
    public sealed record Occurrence(
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        DateOnly LocalDate
    );

    /// <param name="rrule">RFC 5545 recurrence rule, e.g. FREQ=WEEKLY;BYDAY=MO,TU</param>
    /// <param name="anchorDate">First local date the rule can apply (DTSTART's date part)</param>
    /// <param name="exDates">Local dates carved out (EXDATE) - holiday closures</param>
    public static IReadOnlyList<Occurrence> Expand(
        string rrule,
        DateOnly anchorDate,
        TimeOnly startLocal,
        TimeOnly endLocal,
        string ianaTimeZone,
        IEnumerable<DateOnly> exDates,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc
    )
    {
        // Overnight windows (22:00-02:00) end on the next local day.
        var endDate = endLocal <= startLocal ? anchorDate.AddDays(1) : anchorDate;
        var calendarEvent = new CalendarEvent
        {
            DtStart = ToCal(anchorDate, startLocal, ianaTimeZone),
            DtEnd = ToCal(endDate, endLocal, ianaTimeZone),
            RecurrenceRule = new RecurrencePattern(rrule),
        };
        foreach (var exDate in exDates)
            calendarEvent.ExceptionDates.Add(ToCal(exDate, startLocal, ianaTimeZone));

        return
        [
            .. calendarEvent
                .GetOccurrences(new CalDateTime(horizonStartUtc.UtcDateTime, "UTC"))
                .TakeWhile(o => o.Period.StartTime.AsUtc < horizonEndUtc.UtcDateTime)
                .Select(o => new Occurrence(
                    new DateTimeOffset(o.Period.StartTime.AsUtc, TimeSpan.Zero),
                    new DateTimeOffset(
                        (o.Period.EffectiveEndTime ?? o.Period.StartTime).AsUtc,
                        TimeSpan.Zero
                    ),
                    DateOnly.FromDateTime(o.Period.StartTime.Value)
                )),
        ];
    }

    public static bool IsValidRule(string rrule)
    {
        try
        {
            _ = new RecurrencePattern(rrule);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static CalDateTime ToCal(DateOnly date, TimeOnly time, string tz) =>
        new(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, tz);
}
