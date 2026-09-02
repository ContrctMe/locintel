using LocIntel.Contracts;
using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Alerts.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Alerts.Alerts;

/// <summary>
/// High and Critical incidents become alerts the moment they are filed, and
/// the org's managers get an email (v1 transport; SMS and push are the
/// fork-territory transports the blueprint names). Tenant from the envelope.
/// </summary>
public static class IncidentReportedHandler
{
    [Transactional(typeof(AlertsDbContext))]
    public static async Task Handle(
        IncidentReported message,
        Envelope envelope,
        ITenantContext tenant,
        AlertsDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"IncidentReported arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        if (message.Severity is not ("High" or "Critical"))
            return;
        var alert = new Alert
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Kind = AlertKind.HighSeverityIncident,
            Severity = message.Severity == "Critical" ? AlertSeverity.Critical : AlertSeverity.High,
            Title =
                $"{message.Severity} {Spaced(message.Category).ToLowerInvariant()} at {message.SiteName}",
            Body = message.Title,
            Path = new LTree(message.Path),
            IncidentId = message.IncidentId,
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new SendOrgNotice(
                alert.Title,
                [
                    message.Title,
                    $"Occurred {message.OccurredAt:u}.",
                    "Open the console for the full report.",
                ],
                "alerts",
                alert.Title
            ),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
    }

    private static string Spaced(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
}
