using LocIntel.Platform.Kernel;
using Wolverine;

namespace LocIntel.Platform.Messaging;

/// <summary>
/// Publishes with the org on the ENVELOPE (ADR 24) - never inside the message.
/// Lives in Platform because every module needs it: it used to sit in Tenancy,
/// so the other modules hand-rolled `new DeliveryOptions { TenantId = ... }`
/// and one of them could have got it wrong in silence.
/// </summary>
public static class TenantedMessaging
{
    public static ValueTask PublishForOrgAsync<T>(this IMessageBus bus, OrgId org, T message)
        where T : notnull =>
        bus.PublishAsync(message, new DeliveryOptions { TenantId = org.Value.ToString() });

    /// <summary>
    /// One envelope-tenanted copy of <paramref name="message"/> to EACH org -
    /// the push half of cross-tenant sharing (ADR 48, docs/cross-tenant-sharing.md).
    /// This is how an owner's aggregate reaches the orgs it names: each copy
    /// lands under that org's RLS session, and the handler materializes that
    /// org's own row.
    ///
    /// <paramref name="correlationId"/> is the owner-side identity of the thing
    /// being shared (a request id, a share id). It rides the envelope so every
    /// recipient's handler can key its upsert on (correlationId, own org) - which
    /// is what makes a redelivered fan-out land once, not twice. Duplicate orgs
    /// in the list collapse to one copy. Use this for a CHOSEN list; for
    /// "open to everyone" publish a platform-global projection instead of
    /// pushing into every tenant - see the recipe for why.
    /// </summary>
    public static async ValueTask FanOutAsync<T>(
        this IMessageBus bus,
        IEnumerable<OrgId> orgs,
        T message,
        Guid correlationId
    )
        where T : notnull
    {
        foreach (var (_, options) in FanOut.Plan(orgs, correlationId))
            await bus.PublishAsync(message, options);
    }
}

/// <summary>
/// The pure part of a fan-out, kept separate so it can be tested as logic:
/// which orgs receive a copy, and what each envelope carries.
/// </summary>
public static class FanOut
{
    public static IReadOnlyList<(OrgId Org, DeliveryOptions Options)> Plan(
        IEnumerable<OrgId> orgs,
        Guid correlationId
    )
    {
        var plan = new List<(OrgId, DeliveryOptions)>();
        var seen = new HashSet<OrgId>();
        foreach (var org in orgs)
        {
            if (!seen.Add(org))
                continue; // a repeated recipient is one recipient
            plan.Add(
                (
                    org,
                    new DeliveryOptions
                    {
                        TenantId = org.Value.ToString(),
                        CorrelationId = correlationId.ToString(),
                        // transports with native deduplication (queues that
                        // support it) drop a redelivered copy on this key; on
                        // the Postgres transport the handler's upsert on
                        // (correlationId, org) is what makes it effectively-once
                        DeduplicationId = $"{correlationId:N}:{org.Value:N}",
                    }
                )
            );
        }
        return plan;
    }
}
