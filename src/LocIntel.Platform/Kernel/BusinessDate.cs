namespace LocIntel.Platform.Kernel;

/// <summary>
/// The site-local business date (ADR 26): "yesterday's numbers per store" is a
/// group-by on a stamped column, never a per-row conversion at read time. Fact
/// rows call this at WRITE time with the site's IANA zone.
/// </summary>
public static class BusinessDate
{
    public static DateOnly For(DateTimeOffset instant, string ianaTimeZone)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    /// <summary>Validates an IANA zone id ("America/New_York").</summary>
    public static bool IsValidTimeZone(string ianaTimeZone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(ianaTimeZone, out _);
}
