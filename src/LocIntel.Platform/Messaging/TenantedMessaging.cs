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
}
