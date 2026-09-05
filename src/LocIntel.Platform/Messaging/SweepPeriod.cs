namespace LocIntel.Platform.Messaging;

/// <summary>
/// The period bucket an instant falls in, for an interval: aligned to the
/// epoch so every replica computes the same bucket from its own clock and
/// its own timer phase. Pure, so the alignment is unit-tested.
/// </summary>
public static class SweepPeriod
{
    public static DateTimeOffset Of(DateTimeOffset now, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "an interval must be positive");
        var ticks = now.UtcTicks - now.UtcTicks % interval.Ticks;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
