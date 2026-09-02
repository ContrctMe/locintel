using System.Text.Json;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Wolverine;

namespace LocIntel.Contracts;

/// <summary>
/// Publishing a domain audit record is a fixed ceremony: serialize the
/// payload, tenant the envelope, and attach the actor headers. It was written
/// out 27 times in this codebase, and a fork grew five near-identical
/// per-module helpers plus inline copies on top of that. Every copy is a
/// chance to omit the tenant (the record lands untenanted) or the actor (the
/// trail says a thing happened but not who did it).
///
/// It lives beside the message rather than in Platform because Platform sits
/// BELOW Contracts and must not know RecordDomainAudit.
/// </summary>
public static class AuditPublishing
{
    public static ValueTask AuditAsync<TPayload>(
        this IMessageBus bus,
        OrgId org,
        AuditActor actor,
        string eventName,
        TPayload payload
    )
    {
        var options = new DeliveryOptions { TenantId = org.Value.ToString() };
        options.Headers["locintel-actor-tier"] = actor.Tier;
        if (actor.Id is { } id)
            options.Headers["locintel-actor-id"] = id.ToString();

        return bus.PublishAsync(
            new RecordDomainAudit(eventName, JsonSerializer.Serialize(payload)),
            options
        );
    }
}
