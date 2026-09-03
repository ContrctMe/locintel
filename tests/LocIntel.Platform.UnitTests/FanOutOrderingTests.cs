namespace LocIntel.Platform.UnitTests;

/// <summary>
/// The other half of the recipe, as a worked example (docs/cross-tenant-
/// sharing.md, "Order is not guaranteed"): the owner's handler applies the
/// other party's actions monotonically, because two fanned-out events can
/// arrive in either order and a redelivery can bring an old one back. This
/// is executable documentation of the DEFAULT a fork should start from -
/// the state machine is the example's, not Platform's.
/// </summary>
public class FanOutOrderingTests
{
    private enum Step
    {
        Submitted,
        Accepted,
        Started,
        Completed,
    }

    private sealed class Request
    {
        public Step Status = Step.Submitted;
        public DateTimeOffset? AcceptedAt;
        public DateTimeOffset? StartedAt;
        public DateTimeOffset? CompletedAt;
    }

    private static readonly DateTimeOffset T1 = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddSeconds(5);

    /// <summary>"The vendor reached this step": applies when the row is before it, and implies every earlier step.</summary>
    private static bool Apply(Request row, Step reached, DateTimeOffset at)
    {
        if (reached <= row.Status)
            return false; // stale - the row already passed this point
        row.AcceptedAt ??= at;
        if (reached >= Step.Started)
            row.StartedAt ??= at;
        if (reached == Step.Completed)
            row.CompletedAt = at;
        row.Status = reached;
        return true;
    }

    [Fact]
    public void A_later_step_arriving_first_advances_and_fills_in_the_skipped_one()
    {
        var row = new Request();

        Assert.True(Apply(row, Step.Started, T2)); // Started overtook Accepted

        Assert.Equal(Step.Started, row.Status);
        Assert.Equal(T2, row.AcceptedAt); // implied by Started: never left null
        Assert.Equal(T2, row.StartedAt);
    }

    [Fact]
    public void The_earlier_step_arriving_late_is_stale_and_changes_nothing()
    {
        var row = new Request();
        Apply(row, Step.Started, T2);

        Assert.False(Apply(row, Step.Accepted, T1));

        Assert.Equal(Step.Started, row.Status);
        Assert.Equal(T2, row.AcceptedAt); // the late timestamp does not rewrite history
    }

    [Fact]
    public void In_order_delivery_is_the_same_end_state()
    {
        // whichever order the outbox chose, the owner converges on one state
        var inOrder = new Request();
        Apply(inOrder, Step.Accepted, T1);
        Apply(inOrder, Step.Started, T2);
        var reversed = new Request();
        Apply(reversed, Step.Started, T2);
        Apply(reversed, Step.Accepted, T1);

        Assert.Equal(inOrder.Status, reversed.Status);
        Assert.Equal(inOrder.StartedAt, reversed.StartedAt);
    }

    [Fact]
    public void A_redelivered_step_is_stale()
    {
        var row = new Request();
        Apply(row, Step.Accepted, T1);

        Assert.False(Apply(row, Step.Accepted, T1));
    }
}
