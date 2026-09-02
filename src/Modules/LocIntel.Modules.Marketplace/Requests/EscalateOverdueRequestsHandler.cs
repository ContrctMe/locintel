using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Vendors;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// Escalates the org's submitted requests whose response window passed:
/// a broadcast widens to the next matching vendors (who get the dispatch,
/// SMS included for emergencies); a direct request nudges the buyer to
/// broadcast instead. Each escalation extends the window; after
/// MaxEscalations the buyer is told the request is unanswered.
/// </summary>
public static class EscalateOverdueRequestsHandler
{
    [Transactional(typeof(MarketplaceDbContext))]
    public static async Task Handle(
        EscalateOverdueRequests _,
        Envelope envelope,
        ITenantContext tenant,
        MarketplaceDbContext db,
        IOptions<MarketplaceOptions> options,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"EscalateOverdueRequests arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var now = time.GetUtcNow();
        var overdue = await db
            .Requests.Where(r =>
                r.OrgId == org
                && r.Status == RequestStatus.Submitted
                && r.ResponseDueAt != null
                && r.ResponseDueAt <= now
            )
            .ToListAsync(ct);
        foreach (var row in overdue)
        {
            row.EscalationCount++;
            row.EscalatedAt = now;
            row.ResponseDueAt = now + options.Value.Window(row.Urgency);
            row.UpdatedAt = now;
            var exhausted = row.EscalationCount > options.Value.MaxEscalations;
            var added = 0;
            if (row.Mode == RequestMode.Broadcast && !exhausted)
            {
                var already = await db
                    .Recipients.Where(x => x.RequestId == row.Id)
                    .Select(x => x.CounterpartyOrgId)
                    .ToListAsync(ct);
                var next = (
                    await VendorMatcher.MatchAsync(
                        db,
                        org,
                        row.Category,
                        row.SiteLatitude,
                        row.SiteLongitude,
                        row.SiteCountryCode,
                        ct
                    )
                )
                    .Where(v => !already.Contains(v))
                    .Take(options.Value.EscalationRecipients)
                    .ToList();
                foreach (var vendorOrg in next)
                {
                    db.Recipients.Add(
                        new RequestRecipient
                        {
                            Id = Guid.CreateVersion7(),
                            OrgId = org,
                            CounterpartyOrgId = vendorOrg,
                            RequestId = row.Id,
                        }
                    );
                    await bus.PublishAsync(
                        new SendOrgNotice(
                            $"Request for quotes ({row.Urgency}): {row.Title}",
                            [
                                $"{row.RequesterName} is still looking for {row.Category} at {row.SiteName}.",
                                $"Starts {row.StartsAt:u}.",
                                "Open the vendor console to quote or decline.",
                            ],
                            "marketplace",
                            row.Urgency == RequestUrgency.Emergency
                                ? $"URGENT request for quotes: {row.Title} at {row.SiteName}"
                                : null
                        ),
                        new DeliveryOptions { TenantId = vendorOrg.Value.ToString() }
                    );
                    added++;
                }
            }
            db.Events.Add(
                new RequestEvent
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = org,
                    CounterpartyOrgId = row.CounterpartyOrgId,
                    RequestId = row.Id,
                    ActorOrgId = org,
                    ActorId = Guid.Empty,
                    Kind = RequestEventKind.StatusChange,
                    Body =
                        exhausted ? "No response after repeated escalation"
                        : row.Mode == RequestMode.Broadcast
                            ? $"No response by the deadline; widened to {added} more vendor(s)"
                        : "No response by the deadline; the vendor was reminded",
                }
            );
            var buyerLines =
                exhausted
                    ? new[]
                    {
                        $"{row.Title} at {row.SiteName} has had no response after {row.EscalationCount - 1} escalations.",
                        "Consider cancelling it or choosing a vendor directly.",
                    }
                : row.Mode == RequestMode.Broadcast
                    ?
                    [
                        $"{row.Title} at {row.SiteName} had no response by the deadline.",
                        added > 0
                            ? $"It was sent to {added} more vendor(s)."
                            : "No further vendors match; consider widening the category or asking directly.",
                    ]
                :
                [
                    $"{row.Title} at {row.SiteName} has not been answered by the vendor.",
                    "Consider broadcasting it for quotes instead.",
                ];
            await bus.PublishAsync(
                new SendOrgNotice(
                    $"Unanswered request: {row.Title}",
                    buyerLines,
                    "marketplace",
                    row.Urgency == RequestUrgency.Emergency
                        ? $"Unanswered emergency request: {row.Title} at {row.SiteName}"
                        : null
                ),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
            if (row.Mode == RequestMode.Direct && row.CounterpartyOrgId is { } vendor && !exhausted)
                await bus.PublishAsync(
                    new SendOrgNotice(
                        $"Reminder: {row.Title} awaits your response",
                        [
                            $"{row.RequesterName} is waiting on {row.Title} at {row.SiteName}.",
                            "Accept or decline in the vendor console.",
                        ],
                        "marketplace",
                        row.Urgency == RequestUrgency.Emergency
                            ? $"URGENT: {row.Title} at {row.SiteName} awaits your response"
                            : null
                    ),
                    new DeliveryOptions { TenantId = vendor.Value.ToString() }
                );
            await bus.AuditAsync(
                org,
                AuditActor.System,
                "marketplace.request_escalated",
                new
                {
                    row.Id,
                    row.EscalationCount,
                    Added = added,
                    Exhausted = exhausted,
                }
            );
        }
        if (overdue.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
