using LocIntel.Contracts;
using LocIntel.Contracts.Entities;
using LocIntel.Modules.Alerts.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Alerts.Alerts;

/// <summary>
/// A person or vehicle tied to a second incident is the pattern the whole
/// product exists to surface: raise a feed alert on the incident's path
/// and tell the managers. The alert names the record; opening it still
/// passes need-to-know. Tenant from the envelope.
/// </summary>
public static class EntityLinkedHandler
{
    [Transactional(typeof(AlertsDbContext))]
    public static async Task Handle(
        EntityLinked message,
        Envelope envelope,
        ITenantContext tenant,
        AlertsDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"EntityLinked arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        if (message.LinkCount < 2)
            return;
        // one alert per (entity, incident): a re-link never repeats the alert
        if (
            await db.Alerts.AnyAsync(
                a => a.EntityId == message.EntityId && a.IncidentId == message.IncidentId,
                ct
            )
        )
            return;
        var alert = new Alert
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Kind = AlertKind.RepeatOffender,
            Severity = message.LinkCount >= 3 ? AlertSeverity.High : AlertSeverity.Medium,
            Title =
                $"Repeat {message.Kind.ToLowerInvariant()}: {message.DisplayName} linked to {message.LinkCount} incidents",
            Body = message.IncidentTitle,
            Path = new LTree(message.Path),
            IncidentId = message.IncidentId,
            EntityId = message.EntityId,
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new SendOrgNotice(
                alert.Title,
                [message.IncidentTitle, "Open the record in the console to review the pattern."],
                "alerts",
                alert.Title
            ),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
    }
}
