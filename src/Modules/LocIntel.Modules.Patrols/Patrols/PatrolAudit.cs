using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Platform.Kernel;
using Wolverine;

namespace LocIntel.Modules.Patrols.Patrols;

public static class PatrolAudit
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
