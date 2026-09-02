using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Incidents.Data;
using LocIntel.Modules.Incidents.Incidents;
using LocIntel.Modules.Incidents.Tips.Api;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Incidents.Tips;

/// <summary>
/// The guest surface for intake (ADR 7): a visitor at the org's public host
/// is a Guest principal holding public:read over that org, which is exactly
/// enough to name a site and leave a tip. The tip lands as an incident with
/// Source = Tip, Medium severity, no reporter - the org's people triage it
/// like any other. Guest rate limits (ADR 30) and a honeypot are the abuse
/// floor; nothing about the org's incidents ever flows back.
/// </summary>
public static class TipEndpoints
{
    public const int MaxDescription = 4000;

    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/public/tips")]
    [ProducesResponseType(typeof(TipReceipt), StatusCodes.Status200OK)]
    public static async Task<IResult> Submit(
        SubmitTipRequest request,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISiteDirectory sites,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.PublicRead, ct))
            return Results.NotFound();
        OrgId? org = accessor.Current switch
        {
            Principal.Guest { Org: { } guestOrg } => guestOrg,
            Principal.Contact contact => contact.Org,
            Principal.User { ActiveOrg: { } activeOrg } => activeOrg,
            _ => null,
        };
        if (org is null)
            return Results.NotFound();
        // honeypot: a filled hidden field is a bot; pretend it worked
        if (!string.IsNullOrWhiteSpace(request.Website))
            return Results.Ok(new TipReceipt(Receipt(Guid.CreateVersion7())));
        var description = request.Description?.Trim() ?? "";
        if (description.Length < 10)
            return Results.BadRequest(
                new { error = "tell us a little more (at least 10 characters)" }
            );
        if (description.Length > MaxDescription)
            return Results.BadRequest(
                new { error = $"tips are limited to {MaxDescription} characters" }
            );
        var site = await sites.FindAsync(request.SiteId, ct);
        if (site is null)
            return Results.NotFound();
        var now = time.GetUtcNow();
        var occurredAt = request.OccurredAt?.ToUniversalTime() ?? now;
        if (occurredAt > now.AddMinutes(5))
            return Results.BadRequest(new { error = "the time cannot be in the future" });

        var incident = new Incident
        {
            Id = Guid.CreateVersion7(),
            OrgId = org.Value,
            SiteId = site.Id,
            HierarchyId = site.HierarchyId,
            Path = new Microsoft.EntityFrameworkCore.LTree(site.Path),
            Category = request.Category,
            Severity = IncidentSeverity.Medium,
            Source = IncidentSource.Tip,
            Title = Title(request.Category, description),
            Narrative = description,
            OccurredAt = occurredAt,
            BusinessDate = BusinessDate.For(occurredAt, site.TimeZone),
            LocalHour = BusinessDate.LocalClock(occurredAt, site.TimeZone).Hour,
            LocalWeekday = BusinessDate.LocalClock(occurredAt, site.TimeZone).Weekday,
            ReportedBy = Guid.Empty,
            ReporterContact = string.IsNullOrWhiteSpace(request.Contact)
                ? null
                : request.Contact.Trim()[..Math.Min(request.Contact.Trim().Length, 320)],
            Tags = ["tip"],
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org.Value,
            new AuditActor("guest", null),
            "incident.tip_received",
            new
            {
                incident.Id,
                incident.SiteId,
                Category = incident.Category.ToString(),
            }
        );
        await bus.PublishAsync(
            new IncidentReported(
                incident.Id,
                incident.SiteId,
                site.Name,
                incident.Path.ToString(),
                incident.Category.ToString(),
                incident.Severity.ToString(),
                incident.Title,
                incident.OccurredAt,
                incident.ReportedBy
            ),
            new DeliveryOptions { TenantId = org.Value.Value.ToString() }
        );
        return Results.Ok(new TipReceipt(Receipt(incident.Id)));
    }

    private static string Title(IncidentCategory category, string description)
    {
        var firstLine = description.Split('\n')[0].Trim();
        var snippet = firstLine.Length > 60 ? firstLine[..57] + "..." : firstLine;
        return $"Tip: {snippet}";
    }

    /// <summary>Short, unguessable, quotable: the tail of a v7 id.</summary>
    private static string Receipt(Guid id) => "TIP-" + id.ToString("N")[^8..].ToUpperInvariant();
}
