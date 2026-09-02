namespace LocIntel.Modules.Entities.Entities;

/// <summary>
/// Retention floor and ceiling for person records (US posture, blueprint):
/// a year by default, three at most; managers may shorten to "now".
/// </summary>
public static class EntityRetention
{
    public const int DefaultDays = 365;
    public const int MaxDays = 3 * 365;

    public static DateTimeOffset Default(DateTimeOffset now) => now.AddDays(DefaultDays);

    public static bool IsAllowed(DateTimeOffset expiresAt, DateTimeOffset now) =>
        expiresAt <= now.AddDays(MaxDays);
}
