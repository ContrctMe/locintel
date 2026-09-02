using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Platform.Kernel;
using Wolverine;

namespace LocIntel.Modules.Marketplace.Marketplace;

/// <summary>Intent-level audit (ADR 12) recorded in the ACTOR's org; the other party's copy is the request timeline.</summary>
public static class MarketplaceAudit
{
    public static ValueTask PublishAsync(
        IMessageBus bus,
        ActorRef actor,
        string eventName,
        object payload
    ) =>
        bus.PublishAsync(
            new RecordDomainAudit(eventName, JsonSerializer.Serialize(payload)),
            new DeliveryOptions
            {
                TenantId = actor.Org.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = actor.Tier,
                    ["locintel-actor-id"] = actor.Id.ToString(),
                },
            }
        );
}
