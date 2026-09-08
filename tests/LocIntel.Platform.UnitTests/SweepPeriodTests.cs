using LocIntel.Platform.Messaging;

namespace LocIntel.Platform.UnitTests;

/// <summary>The period bucket every replica computes from its own clock (ISweepLease).</summary>
public class SweepPeriodTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void Two_instants_in_one_interval_share_a_bucket_across_the_boundary_they_do_not()
    {
        var early = new DateTimeOffset(2026, 9, 3, 10, 5, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2026, 9, 3, 10, 59, 59, TimeSpan.Zero);
        var next = new DateTimeOffset(2026, 9, 3, 11, 0, 0, TimeSpan.Zero);

        Assert.Equal(SweepPeriod.Of(early, Hour), SweepPeriod.Of(late, Hour));
        Assert.NotEqual(SweepPeriod.Of(late, Hour), SweepPeriod.Of(next, Hour));
        Assert.Equal(
            new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero),
            SweepPeriod.Of(early, Hour)
        );
    }

    [Fact]
    public void Buckets_are_aligned_to_the_epoch_not_to_when_a_replica_started()
    {
        // a replica whose timer phase is 10:37 still lands in the 10:00 bucket
        var offset = new DateTimeOffset(2026, 9, 3, 10, 37, 12, TimeSpan.FromHours(-5)); // 15:37Z
        Assert.Equal(
            new DateTimeOffset(2026, 9, 3, 15, 0, 0, TimeSpan.Zero),
            SweepPeriod.Of(offset, Hour)
        );
    }

    [Fact]
    public void A_daily_bucket_is_the_utc_day()
    {
        var day = TimeSpan.FromHours(24);
        var noon = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero),
            SweepPeriod.Of(noon, day)
        );
    }

    [Fact]
    public void A_non_positive_interval_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SweepPeriod.Of(DateTimeOffset.UnixEpoch, TimeSpan.Zero)
        );
    }
}
