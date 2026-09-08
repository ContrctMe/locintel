using LocIntel.Platform.Messaging;

namespace LocIntel.Platform.UnitTests;

/// <summary>The order a projection applies replicated events in (docs/cross-tenant-sharing.md).</summary>
public class ProjectionVersionTests
{
    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(5, 5, false)] // a redelivery is not newer
    [InlineData(1, 0, true)] // the first event over an empty projection
    [InlineData(2_147_483_647, 0, true)]
    [InlineData(2_147_483_648, 0, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(0, 2_147_483_648, false)]
    [InlineData(0, uint.MaxValue, false)]
    [InlineData(uint.MaxValue, 0, true)]
    public void Newer_means_strictly_after_the_applied_version(
        long incoming,
        long applied,
        bool newer
    )
    {
        Assert.Equal(newer, ProjectionVersion.IsNewer(incoming, applied));
    }

    [Fact]
    public void A_wrapped_transaction_id_still_orders()
    {
        // xmin is 32 bits and wraps; PostgreSQL orders ids modulo 2^32 and so do we
        Assert.True(ProjectionVersion.IsNewer(incoming: 3, applied: uint.MaxValue - 2));
        Assert.False(ProjectionVersion.IsNewer(incoming: uint.MaxValue - 2, applied: 3));
    }
}
