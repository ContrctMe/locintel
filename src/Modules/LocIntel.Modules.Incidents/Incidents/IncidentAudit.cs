using System.Text.Json;
using LocIntel.Contracts;
using Wolverine;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>Intent-level audit (ADR 12) in business language; org and actor ride the envelope, never ambient.</summary>
public static class IncidentAudit
{
    public static ValueTask PublishAsync(
        IMessageBus bus,
        IncidentActor actor,
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
