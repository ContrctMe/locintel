using LocIntel.Platform.Kernel;
using Wolverine;

namespace LocIntel.Modules.Tenancy;

/// <summary>Publishes with the org on the ENVELOPE (ADR 24) - never inside the message.</summary>
public static class TenantedMessaging
{
    public static ValueTask PublishForOrgAsync<T>(this IMessageBus bus, OrgId org, T message)
        where T : notnull =>
        bus.PublishAsync(message, new DeliveryOptions { TenantId = org.Value.ToString() });
}
