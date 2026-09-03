using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;

namespace LocIntel.Platform.UnitTests;

/// <summary>
/// The pure half of FanOutAsync (ADR 48, docs/cross-tenant-sharing.md):
/// which orgs get a copy and what each envelope carries. Testable as logic
/// because the bus call is a one-line loop over this plan.
/// </summary>
public class FanOutTests
{
    private static readonly OrgId A = OrgId.New();
    private static readonly OrgId B = OrgId.New();
    private static readonly Guid Correlation = Guid.NewGuid();

    [Fact]
    public void One_copy_per_distinct_org_in_input_order()
    {
        var plan = FanOut.Plan([A, B], Correlation);

        Assert.Equal([A, B], plan.Select(p => p.Org));
    }

    [Fact]
    public void A_repeated_recipient_is_one_recipient()
    {
        // an owner's list may hold the same org twice (two contacts at one
        // vendor, a re-invite); they must not receive two invitations
        var plan = FanOut.Plan([A, B, A, A], Correlation);

        Assert.Equal([A, B], plan.Select(p => p.Org));
    }

    [Fact]
    public void Each_envelope_is_tenanted_to_its_org_and_correlated_to_the_source()
    {
        var plan = FanOut.Plan([A, B], Correlation);

        foreach (var (org, options) in plan)
        {
            // the org rides the ENVELOPE - the handler runs under that org's
            // RLS session and writes that org's own row
            Assert.Equal(org.Value.ToString(), options.TenantId);
            // the owner-side identity, so the recipient's upsert can key on
            // (correlation, own org) and a redelivery lands once
            Assert.Equal(Correlation.ToString(), options.CorrelationId);
        }
    }

    [Fact]
    public void Deduplication_key_is_unique_per_org_and_stable_per_fan_out()
    {
        var first = FanOut.Plan([A, B], Correlation);
        var again = FanOut.Plan([A, B], Correlation);

        // distinct across orgs in one fan-out...
        Assert.NotEqual(first[0].Options.DeduplicationId, first[1].Options.DeduplicationId);
        // ...and identical for the same (correlation, org) on a redelivery, so
        // a transport with native deduplication drops the second copy
        Assert.Equal(first[0].Options.DeduplicationId, again[0].Options.DeduplicationId);
    }

    [Fact]
    public void An_empty_list_is_an_empty_plan_not_an_error()
    {
        Assert.Empty(FanOut.Plan([], Correlation));
    }
}
